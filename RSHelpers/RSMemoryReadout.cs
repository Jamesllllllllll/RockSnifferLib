using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers.NoteData;
using System;

namespace RockSnifferLib.RSHelpers
{
    [Serializable]
    public class RSMemoryReadout
    {
        public float songTimer = 0;

        public string songID = "";
        public string arrangementID = "";
        public string gameStage = "";

        /// <summary>
        /// The user's currently-selected Path (arrangement type) at the menu level.
        /// Read from a stable byte pointer (see MemoryOffsets.GetCurrentPathPointer).
        /// Persistent across game stages, populated from launch, only changes when the
        /// user actively switches Path in options or song-select. Crucially works in
        /// Nonstop Play, where the per-song arrangement_hash pointer fails.
        ///
        /// Raw byte values: 0x01=Lead, 0x02=Rhythm, 0x04=Bass, anything else=Unknown.
        /// `currentPath` (string) is the human-readable form — "Lead", "Rhythm", "Bass",
        /// or "" (empty string) when the byte doesn't match a known value.
        /// </summary>
        public byte currentPathByte = 0;
        public string currentPath = "";

        /// <summary>
        /// Raw value of Rocksmith's pause-menu mode byte (v0.6.7).
        /// See MemoryOffsets.GetPauseMenuModePointer for discovery context and
        /// the full value table. Briefly:
        ///   0 — no blocking overlay (active gameplay, menus, song review)
        ///   1 — sub-overlay (tuner accessed from pause menu, Tools sub-menu, etc.)
        ///   2 — top-level blocking overlay (pause menu, Mixer, Tools menu,
        ///       restart-confirmation prompts)
        ///
        /// Cross-mode validated for SA, LaS, NSP, and Guitarcade. Survives game
        /// relaunch as a .data-section static (no warmup gate, unlike the
        /// GCPauseManager-flag candidate explored earlier).
        ///
        /// Note: value=2 also fires for the main menu's Tools overlay, which has
        /// nothing to do with mid-song pause. Consumers wanting strict
        /// "user is paused during a song" should combine `pauseMenuMode != 0`
        /// with a SnifferState check (e.g. game_state in {SONG_PLAYING,
        /// SONG_PAUSED}). The convenience `isPaused` boolean below is the raw
        /// `pauseMenuMode != 0` test without that gating, suitable for addons
        /// that don't care about main-menu Tools usage.
        /// </summary>
        public byte pauseMenuMode = 0;

        /// <summary>
        /// True when any blocking pause-style overlay is active (v0.6.7).
        /// Derived from `pauseMenuMode != 0`. See `pauseMenuMode` documentation
        /// for the full value table and the caveat about main-menu Tools.
        ///
        /// This is the raw engine signal. For "user is paused during a song"
        /// specifically (filtering out main-menu Tools), consumers should use
        /// SnifferState (game_state), which interprets this flag against the
        /// player's state-machine context.
        /// </summary>
        public bool isPaused = false;

        /// <summary>
        /// Current mode (LearnASong, ScoreAttack, etc.) as a readable string in
        /// the JSON output (v0.6.7). Without StringEnumConverter, Newtonsoft
        /// serializes enum fields as their underlying integer value (0/1/2/...),
        /// which is hard to interpret without the enum definition in hand. With
        /// the converter, the JSON shows `"mode": "SCOREATTACK"` directly.
        ///
        /// API note: any addon that previously compared mode to an integer
        /// literal will break. None of the bundled addons in this repository
        /// do that.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public RSMode mode = RSMode.UNKNOWN;
        public INoteData noteData;

        /// <summary>
        /// Prints out this readouts details (if Logger.logMemoryOutput is enabled)
        /// </summary>
        public void Print()
        {
            if (Logger.logMemoryReadout)
            {
                Logger.Log("SID: {0}\r\nt: {1}, hits: {2}, misses: {3}\r\nstreak: {4}, hstreak: {5}, mstreak:{6}", songID, songTimer, noteData.TotalNotesHit, noteData.TotalNotesMissed, noteData.CurrentHitStreak, noteData.HighestHitStreak, noteData.CurrentMissStreak);
            }
        }

        /// <summary>
        /// Copy the fields from this readout to another
        /// </summary>
        /// <param name="copy">target readout</param>
        internal void CopyTo(ref RSMemoryReadout copy)
        {
            copy.songTimer = songTimer;

            copy.songID = songID;
            copy.arrangementID = arrangementID;
            copy.gameStage = gameStage;

            copy.currentPathByte = currentPathByte;
            copy.currentPath = currentPath;

            copy.pauseMenuMode = pauseMenuMode;
            copy.isPaused = isPaused;

            copy.mode = mode;

            copy.noteData = noteData;
        }

        /// <summary>
        /// Returns a copy of this memory readout
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout Clone()
        {
            RSMemoryReadout copy = new RSMemoryReadout();

            CopyTo(ref copy);

            return copy;
        }
    }
}
