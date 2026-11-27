using RockSnifferLib.Cache;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Multiplayer;
using RockSnifferLib.Sniffing;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class MultiplayerSongResolverTests
{
    private static SongDetails Song(string id, params string[] arrangements) => new() {
        songID = id, songName = id, songLength = 270,
        arrangements = arrangements.Select(id => new ArrangementDetails { arrangementID = id }).ToList(),
    };
    [Fact] public void ResolvesWithoutPreviewAndRejectsStalePreview()
    {
        var cache = new MemoryCache(); var current = Song("current", "lead", "bass");
        cache.Add("current.psarc", new() { [current.songID] = current });
        cache.Add("old.psarc", new() { ["old"] = Song("old", "old-lead", "old-bass") });
        Assert.Same(current, MultiplayerSongResolver.Resolve(cache, null, ["LEAD", "bass"]));
        Assert.Same(current, MultiplayerSongResolver.Resolve(cache, "old", ["lead", "bass"]));
        Assert.Equal(270, MultiplayerSongResolver.Resolve(cache, null, ["lead", "bass"])!.songLength);
        Assert.Same(current, MultiplayerSongResolver.Resolve(cache, null, ["lead", "lead"]));
        Assert.Null(MultiplayerSongResolver.Resolve(cache, null, ["lead", "old-bass"]));
        Assert.Null(MultiplayerSongResolver.Resolve(cache, null, ["lead", ""]));
    }
    [Fact] public void IndexTracksRemovalAndReplacementAndRejectsAmbiguity()
    {
        var cache = new MemoryCache(); var original = Song("one", "lead", "bass");
        cache.Add("one.psarc", new() { [original.songID] = original });
        cache.Add("shared.psarc", new() { [original.songID] = original });
        cache.Add("two.psarc", new() { ["two"] = Song("two", "lead", "bass") });
        Assert.Null(cache.GetByArrangements(["lead", "bass"]));
        cache.Remove("two.psarc", []);
        Assert.Same(original, cache.GetByArrangements(["lead", "bass"]));
        cache.Remove("one.psarc", []);
        Assert.Same(original, cache.GetByArrangements(["lead", "bass"]));
        cache.Remove("shared.psarc", []);
        Assert.Null(cache.GetByArrangements(["lead", "bass"]));
        var replacement = Song("replacement", "new-lead", "new-bass");
        cache.Add("one.psarc", new() { [replacement.songID] = replacement });
        Assert.Null(cache.GetByArrangements(["lead", "bass"]));
        Assert.Same(replacement, cache.GetByArrangements(["new-lead", "new-bass"]));
    }
}
