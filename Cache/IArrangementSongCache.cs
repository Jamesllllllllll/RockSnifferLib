using RockSnifferLib.Sniffing;
using System.Collections.Generic;

namespace RockSnifferLib.Cache;

/// <summary>Optional exact-arrangement lookup for hosts with a loaded local library.</summary>
public interface IArrangementSongCache
{
    SongDetails? GetByArrangements(IEnumerable<string> arrangementIds);
}
