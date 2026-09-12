using System.Text.Json;
using RockSnifferLib.RSHelpers.Multiplayer;
using Xunit;

namespace RockSnifferLib.Tests;

public class MultiplayerCaptureTests
{
    [Theory]
    [InlineData("acoustic-accuracy", 2, 147, 49, 48, 75)]
    [InlineData("electric-accuracy", 1, 865, 379, 66, 69.5337620578778)]
    public void SanitizedCaptureRetainsObservedResults(string fixture, int slot,
        int hits, int misses, int highest, double accuracy)
    {
        var frames = JsonSerializer.Deserialize<MultiplayerFrame[]>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".json")))!;
        var tracker = new MultiplayerTracker();
        MultiplayerSnapshot? snapshot = null;
        var runIds = new HashSet<string>();
        foreach (var frame in frames)
        {
            snapshot = tracker.Update(frame);
            if (snapshot.RunId != null) runIds.Add(snapshot.RunId);
        }
        Assert.Single(runIds);
        Assert.Equal("ended", snapshot!.State);
        Assert.Equal("unknown", snapshot.LastResult!.Outcome); // no metadata duration in fixture
        var player = slot == 1 ? snapshot.LastResult.Player1 : snapshot.LastResult.Player2;
        Assert.Equal(hits, player!.Hits); Assert.Equal(misses, player.Misses);
        Assert.Equal(highest, player.HighestHitStreak);
        Assert.Equal(accuracy, player.Accuracy!.Value, 6);
    }
}
