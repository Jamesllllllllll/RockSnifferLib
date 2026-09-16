using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        public bool enableAutoEnumeration = true;
        /// <summary>Opt-in experimental snapshot for the detected edition; no legacy completion events.</summary>
        public bool enableExperimentalMultiplayer = false;
        /// <summary>Opt-in experimental profile selection/login observation for the detected edition.</summary>
        public bool enableExperimentalProfiles = false;
        /// <summary>Host-supplied saved profile identities; unknown/duplicate names stay unresolved.</summary>
        public RSHelpers.Profiles.ProfileIdentity[] experimentalProfileCatalog =
            Array.Empty<RSHelpers.Profiles.ProfileIdentity>();
        public int parallelism = 0;
    }
}
