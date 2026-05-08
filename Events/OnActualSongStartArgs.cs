using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnActualSongStartArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;
        public string arrangementID;  // Resolved arrangement ID (may be null if unresolved)
        public string path;           // Arrangement type (Lead/Rhythm/Bass)
        public string tuning;         // Tuning (e.g., "E Standard", "D Standard (Capo Fret 2)")
        // True if the song started while Rocksmith was in a Nonstop Play gameStage
        // (nsp_main / nonstopplayhub / nonstopplaygame). Used to gate playthrough
        // tracking — we don't write history or per-attempt records for Nonstop because
        // arrangement resolution is unreliable in that mode (the arrangement_hash
        // memory pointer doesn't populate in Nonstop, and bonus/alternate arrangements
        // can also be enabled). Reverted writes once Nonstop arrangement resolution
        // is reliable.
        public bool wasNonstopMode;
    }
}
