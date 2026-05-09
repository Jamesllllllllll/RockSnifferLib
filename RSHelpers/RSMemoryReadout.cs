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
