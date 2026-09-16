using RockSnifferLib.RSHelpers.Multiplayer;
using Xunit;

namespace RockSnifferLib.Tests;

public class MultiplayerTrackerTests
{
    private static PlayerRead Player(int slot, int hits = 0, int misses = 0,
        string id = "11111111111111111111111111111111", uint? address = null) =>
        new(address ?? (uint)(0x10000 * slot), new(slot, id, hits, misses, 0, 0, 0));
    private static MultiplayerFrame F(double at, double? time, int? pause = 0,
        PlayerRead? p1 = null, PlayerRead? p2 = null, string stage = "panel_bib") =>
        new(at, stage, pause, time, time, p1 ?? Player(1), p2 ?? Player(2), "test", 240);

    [Fact] public void ObservedAccuracyAndStreakAreIndependent()
    {
        var player2 = new PlayerSnapshot(2, "test", 147, 49, 9, 48, 0);
        var player1 = new PlayerSnapshot(1, "test", 865, 379, 2, 66, 0);
        Assert.Equal(75, player2.Accuracy);
        Assert.Equal(69.533762, player1.Accuracy!.Value, 6);
        Assert.Equal(66, player1.HighestHitStreak);
        Assert.Null(new PlayerSnapshot(1, "test", 0, 0, 0, 0, 0).Accuracy);
        Assert.Equal(0, new PlayerSnapshot(1, "test", 0, 10, 0, 0, 10).Accuracy);
    }

    [Theory] [InlineData(.2)] [InlineData(1.0)]
    public void StickyStagePauseAndRewindDoNotCreateANewRun(double cadence)
    {
        var t = new MultiplayerTracker();
        t.Update(F(0, 10));
        var playing = t.Update(F(cadence, 10 + cadence));
        Assert.Equal("playing", playing.State);
        var paused = t.Update(F(2 * cadence, 10 + cadence, 2));
        Assert.Equal("paused", paused.State);
        var resume = t.Update(F(3 * cadence, 8, 0));
        Assert.NotEqual("playing", resume.State);
        resume = t.Update(F(4 * cadence, 8 + cadence, 0));
        Assert.Equal("playing", resume.State);
        Assert.Equal(playing.RunId, resume.RunId);
    }

    [Fact] public void AliasAndMissingReadsCannotOverwriteFinalStats()
    {
        var t = new MultiplayerTracker();
        var p1 = Player(1, 147, 49); var p2 = Player(2, 0, 20);
        t.Update(F(0, 239, p1: p1, p2: p2));
        var live = t.Update(F(.2, 239.966, p1: p1, p2: p2));
        var alias = t.Update(F(.4, null, p1: p1, p2: p2 with { Address = p1.Address }));
        Assert.Equal("unavailable", alias.State);
        Assert.Null(alias.Player1);
        var ended = t.Update(F(1.2, null, p1: p1, p2: p2 with { Address = p1.Address }));
        Assert.Equal("ended", ended.State);
        Assert.Equal("completed", ended.LastResult!.Outcome);
        Assert.Equal(147, ended.LastResult.Player1!.Hits);
        Assert.Null(ended.Player1);
        Assert.Equal(147, live.Player1!.Hits); // published snapshot is immutable
    }

    [Fact] public void EarlyExitIsNotCompletionAndSinglePlayerClearsSlots()
    {
        var t = new MultiplayerTracker();
        t.Update(F(0, 15)); t.Update(F(.2, 16));
        var exit = t.Update(F(.4, 16, stage: "las_options"));
        Assert.Equal("inactive", exit.State);
        Assert.Equal("abandoned", exit.LastResult!.Outcome);
        Assert.Null(exit.RunId); Assert.Null(exit.Player2); Assert.Null(exit.ElapsedSeconds);
    }

    [Fact] public void SameArrangementIsAllowedButCounterResetBeginsNewRun()
    {
        var t = new MultiplayerTracker();
        var first = t.Update(F(0, 30, 2, Player(1, 10), Player(2, 7)));
        Assert.NotNull(first.Player1); Assert.NotNull(first.Player2);
        var restart = t.Update(F(.2, 0, 2));
        Assert.NotEqual(first.RunId, restart.RunId);
        Assert.Equal("unknown", restart.LastResult!.Outcome);
        Assert.Equal(10, restart.LastResult.Player1!.Hits);
    }

    [Fact] public void NonzeroResetIsDiscontinuityNotFabricatedCumulativeScore()
    {
        var t = new MultiplayerTracker();
        t.Update(F(0, 48.21, 2, Player(1, 29, 8), Player(2, 0, 4)));
        var reset = t.Update(F(.2, 40.553, 2));
        Assert.True(reset.HasDiscontinuity);
        Assert.Equal(0, reset.Player1!.Hits);
        Assert.Null(reset.Player1.Accuracy);
        t.Update(F(.4, 239.966));
        var end = t.Update(F(1, null));
        end = t.Update(F(2, null));
        Assert.Equal("unknown", end.LastResult!.Outcome);
    }

    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    [InlineData(1e-22)] [InlineData(5.057646e13)] [InlineData(-1)]
    public void InvalidTimersNeverStart(double value)
    {
        var t = new MultiplayerTracker();
        var s = t.Update(F(0, value));
        Assert.Null(s.RunId); Assert.False(s.TimerReliable);
    }

    [Fact] public void MissingPauseDoesNotInventPlayback()
    {
        var t = new MultiplayerTracker();
        t.Update(F(0, 4, null));
        var s = t.Update(F(.2, 4, null));
        Assert.NotEqual("playing", s.State);
    }

    [Fact] public void ChangedSongMayReuseOtherPlayersAddress()
    {
        var t = new MultiplayerTracker();
        var old = t.Update(F(0, 30, 2));
        var next = t.Update(F(.2, 4, 2,
            Player(1, id: "22222222222222222222222222222222", address: 0x20000),
            Player(2, id: "22222222222222222222222222222222", address: 0x30000)));
        Assert.NotEqual(old.RunId, next.RunId);
        Assert.Equal(1, next.Player1!.Slot); Assert.Equal(2, next.Player2!.Slot);
    }

    [Fact] public void ReplayAfterEndingDoesNotInheritCounterDiscontinuity()
    {
        var t = new MultiplayerTracker();
        t.Update(F(0, 239, p1: Player(1, 147, 49)));
        t.Update(F(.2, 239.966, p1: Player(1, 147, 49)));
        t.Update(F(.4, null));
        var ended = t.Update(F(1.2, null));
        var next = t.Update(F(2, 4, 2));
        Assert.NotEqual(ended.RunId, next.RunId);
        Assert.False(next.HasDiscontinuity);
    }
}
