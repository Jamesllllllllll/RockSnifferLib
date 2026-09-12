using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        public bool enableAutoEnumeration = true;
        /// <summary>Opt-in, tested-build-only snapshot; no legacy completion events.</summary>
        public bool enableExperimentalMultiplayer = false;
        public int parallelism = 0;
    }
}
