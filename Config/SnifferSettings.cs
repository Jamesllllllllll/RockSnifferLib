using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        /// <summary>Opt-in, tested-build-only multiplayer snapshots, separate from single-player scores.</summary>
        public bool enableExperimentalMultiplayer = false;
        public bool enableAutoEnumeration = true;
        public int parallelism = 0;
    }
}
