using RockSnifferLib.Sniffing;
using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RockSnifferLib.RSHelpers.Multiplayer;

/// <summary>
/// Opt-in, read-only reader using the edition supplied by the host. Owns a
/// VM_READ/QUERY_LIMITED_INFORMATION handle, never enumerates or writes memory.
/// Poll about every 100-200ms; snapshots may be displayed at a slower cadence.
/// </summary>
public sealed class ExperimentalMultiplayerReader : IDisposable
{
    private readonly object sync = new();
    private readonly IReadMemory? memory;
    private readonly uint module;
    private readonly ExperimentalMemoryOffsets offsets;
    private readonly MultiplayerTracker tracker = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private string? selectedSongId;
    private bool disposed;
    /// <summary>An edition layout is available; this does not certify live-game validation.</summary>
    public bool Supported { get; }

    public ExperimentalMultiplayerReader(Process process, RSEdition edition)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!ExperimentalMemoryOffsets.TryCreate(edition, out offsets)) return;
        if (!OperatingSystem.IsWindows()) return;
        var image = process.MainModule;
        if (image == null) return;
        module = checked((uint)image.BaseAddress.ToInt64());
        memory = new ProcessReadMemory(process.Id);
        Supported = true;
    }

    internal ExperimentalMultiplayerReader(IReadMemory memory, uint module,
        RSEdition edition = RSEdition.Remastered_Learn_And_Play)
    {
        this.memory = memory;
        this.module = module;
        Supported = ExperimentalMemoryOffsets.TryCreate(edition, out offsets);
    }

    /// <param name="resolveSong">Optional catalog resolver. It receives the last
    /// preview song ID and current arrangement IDs; return null if ambiguous.
    /// The returned song must contain both arrangements to supply identity/time.
    /// No catalog contents are read or modified by this reader itself.</param>
    public MultiplayerSnapshot Read(Func<string?, string[], SongDetails?>? resolveSong = null)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Supported) return new MultiplayerSnapshot();
            var preview = Text(Walk(offsets.SongId, 0xBC, 0), 128);
            if (preview != null && preview.StartsWith("Play_", StringComparison.Ordinal)
                && (preview.EndsWith("_Preview", StringComparison.Ordinal) || preview.EndsWith("_Invalid", StringComparison.Ordinal)))
                selectedSongId = preview.Substring(5, preview.Length - 13);
            var p1 = Player(1, Walk(offsets.GameRoot, 0xB0, 0x30, 0x18, 4, 0x84, 0),
                Walk(offsets.AlternatePlayerRoot, 0x134, 0x88, 0x28, 0, 0x24, 0));
            var p2 = Player(2, Walk(offsets.GameRoot, 0xB0, 0x34, 0x18, 4, 0x74, 0),
                Walk(offsets.AlternatePlayerRoot, 0x134, 0x88, 0x28, 4, 0x24, 0));
            SongDetails? song = null;
            if (p1 != null && p2 != null && p1.Address != p2.Address)
            {
                var ids = new[] { p1.Stats.ArrangementId, p2.Stats.ArrangementId };
                song = resolveSong?.Invoke(selectedSongId, ids);
                if (song != null && !ids.All(id => song.arrangements.Any(a =>
                    string.Equals(a.arrangementID, id, StringComparison.OrdinalIgnoreCase)))) song = null;
            }
            p1 = Enrich(p1, song); p2 = Enrich(p2, song);
            var pause = Bytes(module + offsets.Pause, 1);
            return tracker.Update(new MultiplayerFrame(clock.Elapsed.TotalSeconds,
                Text(module + offsets.Stage, 64), pause == null ? null : pause[0],
                Float(Walk(offsets.GameRoot, 0xB0, 0x30, 0x538, 8)),
                Float(Walk(offsets.GameRoot, 0xB0, 0x34, 0x538, 8)), p1, p2,
                song?.songID, song != null && float.IsFinite(song.songLength) && song.songLength > 0 ? song.songLength : null));
        }
    }

    private static PlayerRead? Enrich(PlayerRead? player, SongDetails? song)
    {
        var arrangement = song?.arrangements.FirstOrDefault(a =>
            string.Equals(a.arrangementID, player?.Stats.ArrangementId, StringComparison.OrdinalIgnoreCase));
        return player == null ? null : player with { Stats = player.Stats with
            { Path = arrangement?.type, Tuning = arrangement?.tuning?.TuningName } };
    }

    private PlayerRead? Player(int slot, uint a, uint b)
    {
        if (a == 0 || a != b) return null;
        var first = ReadPlayer(slot, a);
        var second = ReadPlayer(slot, b);
        return first != null && first == second ? new PlayerRead(a, first) : null;
    }

    private PlayerSnapshot? ReadPlayer(int slot, uint address)
    {
        var bytes = Bytes(address, 0x48);
        if (bytes == null || BitConverter.ToInt32(bytes, 8) != 111000) return null;
        var guid = new Guid(bytes.AsSpan(0x20, 16));
        var h = BitConverter.ToInt32(bytes, 0x30); var m = BitConverter.ToInt32(bytes, 0x40);
        var c = BitConverter.ToInt32(bytes, 0x34); var best = BitConverter.ToInt32(bytes, 0x3C);
        var miss = BitConverter.ToInt32(bytes, 0x44);
        if (guid == Guid.Empty || h < 0 || m < 0 || c < 0 || best < c || best > h || miss < 0 || miss > m) return null;
        return new PlayerSnapshot(slot, guid.ToString("N").ToUpperInvariant(), h, m, c, best, miss);
    }

    private byte[]? Bytes(uint address, int size) => address < 65536
        || size < 1 || size > 128 || (ulong)address + (uint)size > uint.MaxValue
        ? null : memory!.Read(address, size);
    private uint Walk(uint root, params uint[] offsets)
    {
        uint address = checked(module + root);
        foreach (uint offset in offsets)
        {
            var bytes = Bytes(address, 4);
            if (bytes == null) return 0;
            uint next = BitConverter.ToUInt32(bytes, 0);
            if (next < 65536 || (ulong)next + offset > uint.MaxValue) return 0;
            address = next + offset;
        }
        return address;
    }
    private double? Float(uint address)
    {
        var bytes = Bytes(address, 4);
        if (bytes == null) return null;
        float value = BitConverter.ToSingle(bytes, 0);
        return float.IsFinite(value) ? value : null;
    }
    private string? Text(uint address, int length)
    {
        var bytes = Bytes(address, length);
        if (bytes == null) return null;
        int end = Array.IndexOf(bytes, (byte)0);
        if (end < 0 || bytes.Take(end).Any(b => b < 32 || b > 126)) return null;
        return Encoding.ASCII.GetString(bytes, 0, end);
    }
    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; memory?.Dispose(); }
    }
}
