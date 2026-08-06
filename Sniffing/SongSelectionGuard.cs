using System;

namespace RockSnifferLib.Sniffing
{
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
    }
}
