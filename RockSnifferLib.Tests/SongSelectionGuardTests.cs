using RockSnifferLib.Sniffing;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class SongSelectionGuardTests
{
    [Fact]
    public void UnknownChangedSongClearsPreviousDetails()
    {
        var previous = CreateSong("previous-song");

        var resolved = SongSelectionGuard.ResolveDetails(
            "unknown-song",
            previous,
            null
        );

        Assert.NotSame(previous, resolved);
        Assert.Equal("unknown-song", resolved.songID);
        Assert.False(resolved.IsValid());
        Assert.False(
            SongSelectionGuard.MatchesSelectedSong(
                previous,
                "unknown-song"
            )
        );
    }

    [Fact]
    public void TemporarilyMissingCacheKeepsMatchingCurrentDetails()
    {
        var current = CreateSong("current-song");

        var resolved = SongSelectionGuard.ResolveDetails(
            "current-song",
            current,
            null
        );

        Assert.Same(current, resolved);
        Assert.True(
            SongSelectionGuard.MatchesSelectedSong(
                resolved,
                "current-song"
            )
        );
    }

    [Fact]
    public void LaterCacheResolutionReplacesUnresolvedDetails()
    {
        var unresolved = new SongDetails { songID = "new-song" };
        var cached = CreateSong("new-song");

        var resolved = SongSelectionGuard.ResolveDetails(
            "new-song",
            unresolved,
            cached
        );

        Assert.Same(cached, resolved);
        Assert.True(
            SongSelectionGuard.MatchesSelectedSong(resolved, "new-song")
        );
    }

    [Fact]
    public void MismatchedCacheEntryIsNotPublishable()
    {
        var previous = CreateSong("previous-song");
        var mismatched = CreateSong("different-song");

        var resolved = SongSelectionGuard.ResolveDetails(
            "new-song",
            previous,
            mismatched
        );

        Assert.False(resolved.IsValid());
        Assert.False(
            SongSelectionGuard.MatchesSelectedSong(resolved, "new-song")
        );
    }

    private static SongDetails CreateSong(string songID)
    {
        return new SongDetails
        {
            songID = songID,
            songName = "Song",
            artistName = "Artist",
            songLength = 180,
        };
    }
}
