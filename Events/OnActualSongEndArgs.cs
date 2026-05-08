using RockSnifferLib.RSHelpers;
using RockSnifferLib.Sniffing;
using System;

namespace RockSnifferLib.Events
{
    public class OnActualSongEndArgs : EventArgs
    {
        public SongDetails song;
        public DateTime timestamp;
        public bool completed;
        public bool paused;
        // Arrangement context captured at song START (preserved through end-of-song
        // even if memory pointer for arrangementID has been invalidated/cleared by the
        // cross-reference logic during a Nonstop Play song-to-song transition).
        public string arrangementID;
        public string path;
        public string tuning;
        // True if the song was started while Rocksmith was in a Nonstop Play gameStage.
        // Captured at song START in Sniffer.cs and preserved through end (via the run-context
        // fields), so end-of-song handlers know whether to skip playthrough tracking.
        public bool wasNonstopMode;
        // Snapshot of the memory readout at the moment of LogSongEnd. Allows the
        // playthrough history layer to read accurate end-of-song noteData even if
        // currentMemoryReadout has since been updated to the next song's data.
        public RSMemoryReadout readout;
    }
}
