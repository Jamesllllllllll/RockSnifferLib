using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Multiplayer;
using RockSnifferLib.SysHelpers;
using Xunit;

namespace RockSnifferLib.Tests;

// Optional private evidence replay. The capture is never copied into the source
// tree or CI. GUID aliases retain identity but cannot certify catalog membership.
public sealed class MultiplayerPrivateReplayTests
{
    private sealed class Memory : IReadMemory
    {
        internal readonly Dictionary<uint, byte> Data = new();
        internal void Put(uint address, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) Data[address + (uint)i] = bytes[i];
        }
        public byte[]? Read(uint address, int size)
        {
            var result = new byte[size];
            for (int i = 0; i < size; i++)
                if (!Data.TryGetValue(address + (uint)i, out result[i])) return null;
            return result;
        }
        public void Dispose() { }
    }

    private static uint Address(JsonElement node) => node.ValueKind == JsonValueKind.String
        ? uint.Parse(node.GetString()!.AsSpan(2), NumberStyles.HexNumber) : 0;
    private static string? Text(JsonElement node, string property) => node.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Integer(JsonElement node, string property) => node.TryGetProperty(property, out var value)
        && value.TryGetInt32Safe(out var number) ? number : null;
    private static void Load(Memory memory, JsonElement frame, bool opaqueStageIsReadable)
    {
        memory.Data.Clear();
        var walks = frame.GetProperty("Walks").EnumerateArray().ToArray();
        foreach (var walk in walks)
            foreach (var hop in walk.GetProperty("Hops").EnumerateArray())
                if (hop.GetProperty("Read").GetBoolean())
                    memory.Put(Address(hop.GetProperty("At")), BitConverter.GetBytes(Address(hop.GetProperty("Pointer"))));
        foreach (var key in new[] { "P1Primary", "P2Primary", "P1Secondary", "P2Secondary" })
        {
            var player = frame.GetProperty(key);
            var address = Address(player.GetProperty("Address"));
            var arrangement = Text(player, "Arrangement");
            if (address < 65536 || arrangement == null || Integer(player, "Marker") is not int marker) continue;
            var bytes = new byte[0x48];
            BitConverter.GetBytes(marker).CopyTo(bytes, 8);
            SHA256.HashData(Encoding.UTF8.GetBytes(arrangement)).AsSpan(0, 16).CopyTo(bytes.AsSpan(0x20));
            foreach (var (property, offset) in new[] { ("Hits", 0x30), ("Streak", 0x34), ("Best", 0x3C), ("Misses", 0x40), ("MissStreak", 0x44) })
            {
                if (Integer(player, property) is not int number) throw new InvalidDataException("Partial typed counter record.");
                BitConverter.GetBytes(number).CopyTo(bytes, offset);
            }
            memory.Put(address, bytes);
        }
        foreach (var (property, id) in new[] { ("Timer1", "timer-1"), ("Timer2", "timer-2") })
        {
            var walk = walks.Single(w => Text(w, "Id") == id);
            var address = Address(walk.GetProperty("Address"));
            if (address >= 65536 && frame.GetProperty(property).ValueKind == JsonValueKind.Number)
                memory.Put(address, BitConverter.GetBytes(frame.GetProperty(property).GetSingle()));
        }
        var pause = walks.Single(w => Text(w, "Id") == "pause");
        if (Integer(frame, "Pause") is int value)
            memory.Put(Address(pause.GetProperty("Address")), new[] { (byte)value });
        var stage = walks.Single(w => Text(w, "Id") == "stage");
        var stageText = Text(frame, "Stage");
        if (stageText == null && opaqueStageIsReadable && Text(frame, "StageStatus") == "unrecognized_text")
            stageText = "opaque_stage";
        if (stageText != null)
        {
            var bytes = new byte[64]; Encoding.ASCII.GetBytes(stageText).CopyTo(bytes, 0);
            memory.Put(Address(stage.GetProperty("Address")), bytes);
        }
        var preview = walks.Single(w => Text(w, "Id") == "preview");
        if (Address(preview.GetProperty("Address")) >= 65536 && Text(frame, "PreviewStatus") == "recognized"
            && Text(frame, "Song") is string song)
        {
            var bytes = new byte[128]; Encoding.ASCII.GetBytes($"Play_{song}_Preview").CopyTo(bytes, 0);
            memory.Put(Address(preview.GetProperty("Address")), bytes);
        }
    }

    [PrivateReplayTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayPrivateCaptureThroughMemoryReaderAndTracker(bool opaqueStageIsReadable)
    {
        var input = Environment.GetEnvironmentVariable("ROCKSNIFFER_PRIVATE_REPLAY_ZIP");
        if (string.IsNullOrEmpty(input)) return;
        using var zip = ZipFile.OpenRead(input);
        using (var manifest = new StreamReader(zip.GetEntry("manifest.sha256")!.Open()))
        {
            while (manifest.ReadLine() is string manifestLine)
            {
                if (string.IsNullOrWhiteSpace(manifestLine)) continue;
                var parts = manifestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(3, parts.Length);
                var entry = zip.GetEntry(parts[2])!;
                Assert.Equal(long.Parse(parts[1], CultureInfo.InvariantCulture), entry.Length);
                using var stream = entry.Open();
                Assert.Equal(parts[0].ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(stream)));
            }
        }
        using var sessions = JsonDocument.Parse(zip.GetEntry("sessions.json")!.Open());
        var modules = sessions.RootElement.EnumerateArray().ToDictionary(
            session => Text(session, "id")!, session => {
                Assert.Equal("Remastered", Text(session, "edition"));
                return Address(session.GetProperty("moduleBase"));
            });
        using var inputStream = zip.GetEntry("samples.jsonl")!.Open();
        using var lines = new StreamReader(inputStream);
        string? session = null;
        ExperimentalMultiplayerReader? reader = null;
        var memory = new Memory();
        double now = 0;
        var timeline = new List<object>();
        var outputs = new List<(string Session, string Phase, bool Active, double Ms, MultiplayerSnapshot Snapshot)>();
        try
        {
            while (lines.ReadLine() is string line)
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                Assert.Equal(1, row.GetProperty("schema").GetInt32());
                var nextSession = Text(row, "session")!;
                if (session != nextSession)
                {
                    reader?.Dispose(); session = nextSession;
                    reader = new ExperimentalMultiplayerReader(memory, modules[nextSession], RSEdition.Remastered, () => now);
                }
                now = row.GetProperty("ms").GetDouble() / 1000;
                Load(memory, row.GetProperty("frame"), opaqueStageIsReadable);
                var snapshot = reader!.Read();
                Assert.Null(snapshot.SongId); Assert.Null(snapshot.DurationSeconds);
                Assert.NotEqual("completed", snapshot.LastResult?.Outcome); // No catalog/duration evidence in this ZIP.
                foreach (var (player, key, slot) in new[] { (snapshot.Player1, "P1Primary", 1), (snapshot.Player2, "P2Primary", 2) })
                {
                    if (player == null) continue;
                    var captured = row.GetProperty("frame").GetProperty(key);
                    Assert.Equal(slot, player.Slot);
                    Assert.Equal(Integer(captured, "Hits"), player.Hits);
                    Assert.Equal(Integer(captured, "Misses"), player.Misses);
                    Assert.Equal(Integer(captured, "Best"), player.HighestHitStreak);
                    Assert.Equal(Integer(captured, "Streak"), player.CurrentHitStreak);
                    Assert.Equal(Integer(captured, "MissStreak"), player.CurrentMissStreak);
                    var expectedId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(Text(captured, "Arrangement")!)).AsSpan(0, 16));
                    Assert.Equal(expectedId.ToString("N").ToUpperInvariant(), player.ArrangementId);
                }
                outputs.Add((session!, Text(row, "phase")!, row.GetProperty("active").GetBoolean(), now * 1000, snapshot));
                timeline.Add(new { ms = now * 1000, session, phase = Text(row, "phase"), snapshot });
            }
        }
        finally { reader?.Dispose(); }
        Assert.Equal(10287, outputs.Count);
        if (Environment.GetEnvironmentVariable("ROCKSNIFFER_PRIVATE_REPLAY_BASELINE") == "1")
        {
            Assert.All(outputs, row => Assert.Null(row.Snapshot.RunId));
        }
        else
        {
            foreach (var phase in new[] { "p1", "p2", "mixed", "swap-p1", "swap-p2", "different-p1", "different-p2", "restart-1-p1", "restart-1-p2", "restart-2-p1", "restart-2-p2" })
                Assert.Contains(outputs, row => row.Phase == phase && row.Snapshot.State == "playing");
            var hold = outputs.Where(row => row.Phase == "pause-hold").Select(row => row.Snapshot).ToArray();
            Assert.All(hold, snapshot => Assert.Equal("paused", snapshot.State));
            Assert.Single(hold.Select(snapshot => snapshot.RunId).Distinct());
            Assert.Single(hold.Select(snapshot => snapshot.ElapsedSeconds).Distinct());
            Assert.All(outputs.Where(row => row.Snapshot.Stage is "las_game" or "las_songs" or "mp_main"),
                row => { Assert.Null(row.Snapshot.Player1); Assert.Null(row.Snapshot.Player2); });
            foreach (var prefix in new[] { "", "swap-", "different-", "restart-1-", "restart-2-" })
            {
                foreach (var playingSlot in new[] { 1, 2 })
                {
                    var phase = prefix + "p" + playingSlot;
                    var snapshots = outputs.Where(row => row.Phase == phase && row.Active && row.Snapshot.Player1 != null && row.Snapshot.Player2 != null).Select(row => row.Snapshot).ToArray();
                    Assert.NotEmpty(snapshots);
                    Assert.Single(snapshots.Select(snapshot => snapshot.RunId).Distinct());
                    var first = snapshots.First(); var last = snapshots.Last();
                    var firstPlaying = playingSlot == 1 ? first.Player1! : first.Player2!;
                    var lastPlaying = playingSlot == 1 ? last.Player1! : last.Player2!;
                    var firstSilent = playingSlot == 1 ? first.Player2! : first.Player1!;
                    var lastSilent = playingSlot == 1 ? last.Player2! : last.Player1!;
                    Assert.True(lastPlaying.Hits > firstPlaying.Hits, phase);
                    Assert.Equal(firstSilent.Hits, lastSilent.Hits);
                }
            }
            var firstFinish = outputs.Last(row => row.Phase == "natural-end").Snapshot.LastResult!;
            Assert.Equal(375, firstFinish.Player1!.HighestHitStreak);
            Assert.Equal(1168, firstFinish.Player1.Hits);
            Assert.Equal(1305, firstFinish.Player2!.Hits);
            var secondFinish = outputs.Last(row => row.Phase == "different-end").Snapshot.LastResult!;
            Assert.Equal(85, secondFinish.Player1!.HighestHitStreak);
            Assert.Equal(141, secondFinish.Player2!.HighestHitStreak);
            Assert.Equal(601, secondFinish.Player1.Hits);
            Assert.Equal(299, secondFinish.Player2.Hits);
            Assert.Null(outputs.Last(row => row.Phase == "single-exit").Snapshot.Player1);
        }
        var result = new {
            stageInterpretation = opaqueStageIsReadable ? "opaque readable text (outside gameplay)" : "unreadable text (unknown stage)",
            samples = outputs.Count,
            sessions = outputs.Select(row => row.Session).Distinct().Count(),
            states = outputs.GroupBy(row => row.Snapshot.State).ToDictionary(g => g.Key, g => g.Count()),
            phases = outputs.GroupBy(row => row.Phase).Select(g => new {
                phase = g.Key, states = g.GroupBy(row => row.Snapshot.State).ToDictionary(s => s.Key, s => s.Count()),
                runs = g.Select(row => row.Snapshot.RunId).Where(id => id != null).Distinct().Count(),
                lastResult = g.Last().Snapshot.LastResult,
            }).ToArray(),
        };
        var output = Environment.GetEnvironmentVariable("ROCKSNIFFER_PRIVATE_REPLAY_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            output = Path.Combine(output, opaqueStageIsReadable ? "opaque-stage" : "unavailable-stage");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllLines(Path.Combine(output, "timeline.jsonl"), timeline.Select(row => JsonSerializer.Serialize(row)));
        }
    }
}

internal static class ReplayJsonExtensions
{
    internal static bool TryGetInt32Safe(this JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }
}

public sealed class PrivateReplayTheoryAttribute : TheoryAttribute
{
    public PrivateReplayTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROCKSNIFFER_PRIVATE_REPLAY_ZIP")))
            Skip = "Requires an explicitly supplied private capture; no capture is distributed with source.";
    }
}
