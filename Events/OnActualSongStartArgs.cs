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
        // (nsp_main / nonstopplayhub / nonstopplaygame). Set by Sniffer.cs at song
        // start. Pre-v0.6.8 used to gate playthrough tracking — that gate was lifted
        // in v0.6.8 once PLAY_arrID made arrangement resolution reliable in Nonstop.
        // The field is preserved for any downstream consumer that wants the
        // contextual flag.
        public bool wasNonstopMode;

        // (v0.6.8) True if the song started while Rocksmith was in a Multiplayer
        // gameStage (split_game, mp_*, duet_*, h2h_* — i.e. RSMode.MULTIPLAYER).
        // Used to gate playthrough tracking — multi-user note data and per-user
        // arrangements are not currently tracked, so MP plays would produce
        // row-quality issues if persisted. Captured at song start using the
        // gameStage-derived mode field; see Sniffer.cs.
        public bool wasMultiplayerMode;
    }
}
