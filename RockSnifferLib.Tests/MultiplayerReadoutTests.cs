using Newtonsoft.Json.Linq;
using RockSnifferLib.Configuration;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Multiplayer;
using RockSnifferLib.RSHelpers.NoteData;
using Xunit;

namespace RockSnifferLib.Tests;

public class MultiplayerReadoutTests
{
    [Theory]
    [InlineData("loading")]
    [InlineData("playing")]
    [InlineData("paused")]
    [InlineData("unavailable")]
    [InlineData("ended")]
    public void ActiveMultiplayerDoesNotReadOrPublishLegacySinglePlayerCounters(string state)
    {
        var snapshot = new MultiplayerSnapshot { Supported = true, State = state,
            SongId = "synthetic", Stage = "split_game", Player1 = new(1, "one", 7, 3, 2, 4, 1),
            Player2 = new(2, "two", 3, 1, 1, 2, 1) };
        var readout = MultiplayerReadout.Read(snapshot, () => throw new Exception("Legacy read must be skipped"));
        Assert.Equal(0, readout.songTimer);
        Assert.Equal(0, readout.noteData.TotalNotes);
        Assert.Equal(RSMode.MULTIPLAYER, readout.mode);
        Assert.Equal("", readout.arrangementID);
        Assert.Equal(snapshot, readout.experimentalMultiplayer);
        Assert.Equal(snapshot, readout.Clone().experimentalMultiplayer);
        var json = JObject.FromObject(readout);
        Assert.Equal(JTokenType.Integer, json["mode"]!.Type);
        Assert.Equal(3, (int)json["mode"]!);
        Assert.Equal(7, (int)json["experimentalMultiplayer"]!["Player1"]!["Hits"]!);
    }

    [Fact]
    public void DisabledUnsupportedAndInactiveSnapshotsPreserveLegacyBehavior()
    {
        Assert.False(new SnifferSettings().enableExperimentalMultiplayer);
        Assert.Null(new RSMemoryReadout().experimentalMultiplayer);
        foreach (var snapshot in new MultiplayerSnapshot?[] { null, new(), new() { Supported = true } })
        {
            var legacy = new RSMemoryReadout { mode = RSMode.LEARNASONG, songTimer = 12,
                songID = "synthetic", arrangementID = "arrangement", noteData = default(LearnASongNoteData) };
            var result = MultiplayerReadout.Read(snapshot, () => legacy);
            Assert.Same(legacy, result);
            Assert.Equal(12, result.songTimer);
            Assert.Equal("arrangement", result.arrangementID);
            Assert.Equal(RSMode.LEARNASONG, result.mode);
            Assert.False(MultiplayerReadout.IsActive(result.experimentalMultiplayer));
            Assert.Equal(1, (int)JObject.FromObject(result)["mode"]!);
        }
    }
}
