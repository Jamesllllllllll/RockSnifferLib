using RockSnifferLib.RSHelpers.Multiplayer;
using Xunit;

namespace RockSnifferLib.Tests;

public class MultiplayerResultTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TeardownRetainsTheLastCompletePairInsteadOfMixingPartialReads(int missingSlot)
    {
        // Synthetic counts and addresses: no recorded gameplay is needed to
        // verify that one player's newer read cannot overwrite the final pair.
        var player1 = new PlayerRead(0x10000, new(1, "first", 15, 5, 3, 8, 0));
        var player2 = new PlayerRead(0x20000, new(2, "second", 7, 3, 1, 4, 0));
        var tracker = new MultiplayerTracker();
        tracker.Update(new(0, "split_game", 0, 4, 4, player1, player2));
        var playing = tracker.Update(new(.2, "split_game", 0, 4.2, 4.2, player1, player2));

        var partial = tracker.Update(new(.4, "split_game", 0, null, null,
            missingSlot == 1 ? null : player1 with { Stats = player1.Stats with { Hits = 16 } },
            missingSlot == 2 ? null : player2 with { Stats = player2.Stats with { Hits = 8 } }));
        Assert.Equal("unavailable", partial.State);
        Assert.Null(partial.LastResult);

        var ended = tracker.Update(new(1.2, "split_game", 0, null, null, null, null));
        Assert.Equal(playing.RunId, ended.RunId);
        Assert.Equal("ended", ended.State);
        Assert.Null(ended.Player1);
        Assert.Null(ended.Player2);
        var result = ended.LastResult!;
        Assert.Equal("unknown", result.Outcome); // No catalog duration to infer completion.
        Assert.Equal(4.2, result.ElapsedSeconds);
        Assert.Equal(player1.Stats, result.Player1);
        Assert.Equal(player2.Stats, result.Player2);
        Assert.Equal(75, result.Player1!.Accuracy);
        Assert.Equal(70, result.Player2!.Accuracy);

        var later = tracker.Update(new(2, "split_game", 0, null, null, null, null));
        Assert.Equal(result, later.LastResult);
        Assert.Equal(player1.Stats, playing.Player1); // Published snapshots remain unchanged.
        Assert.Equal(player2.Stats, playing.Player2);
    }
}
