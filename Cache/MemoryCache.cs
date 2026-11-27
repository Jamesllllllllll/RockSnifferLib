using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.Cache
{
    public class MemoryCache : ICache, IArrangementSongCache
    {
        private readonly object sync = new();
        private readonly Dictionary<string, Dictionary<SongDetails, int>> arrangements = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Dictionary<string, SongDetails>> Cache { get; } = new Dictionary<string, Dictionary<string, SongDetails>>();

        public void Add(string filepath, Dictionary<string, SongDetails> allDetails)
        {
            lock (sync)
            {
                if (Cache.ContainsKey(filepath)) return;
                Cache.Add(filepath, allDetails);
                foreach (var song in allDetails.Values)
                    foreach (var arrangement in song.arrangements)
                    {
                        var id = arrangement.arrangementID;
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (!arrangements.TryGetValue(id, out var songs))
                            arrangements[id] = songs = new();
                        songs.TryGetValue(song, out int references);
                        songs[song] = references + 1;
                    }
            }
        }

        public void Remove(string filepath, List<string> songIDs)
        {
            lock (sync)
            {
                if (!Cache.Remove(filepath, out var removed)) return;
                foreach (var song in removed.Values)
                    foreach (var arrangement in song.arrangements)
                    {
                        var id = arrangement.arrangementID;
                        if (string.IsNullOrWhiteSpace(id) || !arrangements.TryGetValue(id, out var songs)) continue;
                        if (songs.TryGetValue(song, out int references) && references > 1)
                            songs[song] = references - 1;
                        else songs.Remove(song);
                        if (songs.Count == 0) arrangements.Remove(id);
                    }
            }
        }

        public bool Contains(string filepath, string fileHash)
        {
            lock (sync) return Cache.ContainsKey(filepath);
        }

        public SongDetails Get(string filepath, string songID)
        {
            lock (sync)
                return Cache.TryGetValue(filepath, out var songs) && songs.TryGetValue(songID, out var song) ? song : null;
        }

        public SongDetails Get(string SongID)
        {
            if (string.IsNullOrEmpty(SongID)) return null;
            lock (sync)
            {
                foreach (var songs in Cache.Values)
                    if (songs.TryGetValue(SongID, out var song)) return song;
                return null;
            }
        }

        public SongDetails? GetByArrangements(IEnumerable<string> arrangementIds)
        {
            var ids = arrangementIds.ToArray();
            if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace)) return null;
            lock (sync)
            {
                HashSet<SongDetails>? matches = null;
                foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!arrangements.TryGetValue(id, out var songs)) return null;
                    if (matches == null) matches = new(songs.Keys);
                    else matches.IntersectWith(songs.Keys);
                    if (matches.Count == 0) return null;
                }
                var valid = matches!.Where(song => song.IsValid()).Take(2).ToArray();
                return valid.Length == 1 ? valid[0] : null;
            }
        }
    }
}
