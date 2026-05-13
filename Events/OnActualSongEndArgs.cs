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
        // fields). Pre-v0.6.8 gated playthrough writes; v0.6.8 lifted that gate once
        // PLAY_arrID resolved Nonstop arrangement-ID reliably. Field preserved for
        // any downstream consumer that wants the contextual flag.
        public bool wasNonstopMode;
        // (v0.6.8) True if the song was started while Rocksmith was in a Multiplayer
        // gameStage. Captured at song start in Sniffer.cs and propagated through end
        // so PlaythroughHistory.OnActualSongEnd can gate writes — multi-user note
        // data and per-user arrangements aren't tracked yet, so MP rows would have
        // quality issues. See OnActualSongStartArgs for the full rationale.
        public bool wasMultiplayerMode;
        // Snapshot of the memory readout at the moment of LogSongEnd. Allows the
        // playthrough history layer to read accurate end-of-song noteData even if
        // currentMemoryReadout has since been updated to the next song's data.
        public RSMemoryReadout readout;
    }
}
