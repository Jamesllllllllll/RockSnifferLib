using RockSnifferLib.Cache;
using RockSnifferLib.Sniffing;
using System;
using System.Linq;

namespace RockSnifferLib.RSHelpers.Multiplayer;

internal static class MultiplayerSongResolver
{
    internal static SongDetails? Resolve(ICache cache, string? selectedSongId, string[] ids)
    {
        if (ids.Length != 2 || ids.Any(string.IsNullOrWhiteSpace)) return null;
        var selected = string.IsNullOrWhiteSpace(selectedSongId) ? null : cache.Get(selectedSongId);
        if (selected != null && selected.IsValid() && ids.All(id => selected.arrangements.Any(
            arrangement => string.Equals(arrangement.arrangementID, id, StringComparison.OrdinalIgnoreCase))))
            return selected;
        return (cache as IArrangementSongCache)?.GetByArrangements(ids);
    }
}
