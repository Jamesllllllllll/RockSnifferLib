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
    }
}
