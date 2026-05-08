using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        public bool enableAutoEnumeration = true;
        public int parallelism = 0;

        /// <summary>
        /// User's preferred arrangement type as a Nonstop Play fallback. Used when
        /// the arrangement_hash memory pointer can't resolve the active arrangement
        /// (which is typical in Nonstop — the game doesn't write to the memory cell
        /// the LaS/SA UI reads from). Lower priority than the in-session and
        /// persisted lastResolvedPath: this only kicks in when there is no resolution
        /// history yet (e.g. first session after install, or if state file was
        /// deleted). Set to "Bass", "Lead", or "Rhythm". Empty string disables.
        /// </summary>
        public string defaultArrangementType = "";
    }
}
