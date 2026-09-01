using System;

namespace RockSnifferLib.Sniffing
{
    public static class SongSelectionResolution
    {
        public const string NotDetected = "not_detected";
        public const string Resolved = "resolved";
        public const string CacheMiss = "cache_miss";
        public const string CachedDetailsInvalid = "cached_details_invalid";
        public const string CachedSongIdMismatch = "cached_song_id_mismatch";
    }

    internal static class SongSelectionGuard
    {
        internal static SongDetails ResolveDetails(
            string? selectedSongID,
            SongDetails currentDetails,
            SongDetails? cachedDetails)
        {
            if (MatchesSelectedSong(cachedDetails, selectedSongID))
            {
                return cachedDetails;
            }

            if (!string.IsNullOrWhiteSpace(selectedSongID) &&
                !MatchesSelectedSong(currentDetails, selectedSongID))
            {
                return new SongDetails { songID = selectedSongID };
            }

            return currentDetails;
        }

        internal static bool MatchesSelectedSong(
            SongDetails? details,
            string? selectedSongID)
        {
            return details != null &&
                details.IsValid() &&
                !string.IsNullOrWhiteSpace(selectedSongID) &&
                string.Equals(
                    details.songID,
                    selectedSongID,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        internal static string GetResolution(
            SongDetails? resolvedDetails,
            string? selectedSongID,
            SongDetails? cachedDetails)
        {
            if (string.IsNullOrWhiteSpace(selectedSongID))
            {
                return SongSelectionResolution.NotDetected;
            }

            if (MatchesSelectedSong(resolvedDetails, selectedSongID))
            {
                return SongSelectionResolution.Resolved;
            }

            if (cachedDetails == null)
            {
                return SongSelectionResolution.CacheMiss;
            }

            if (!cachedDetails.IsValid())
            {
                return SongSelectionResolution.CachedDetailsInvalid;
            }

            return SongSelectionResolution.CachedSongIdMismatch;
        }
    }
}
