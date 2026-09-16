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
        /// <summary>Opt-in initial profile selection/login observation for the tested build.</summary>
        public bool enableExperimentalProfiles = false;
        /// <summary>Host-supplied saved profile identities; unknown/duplicate names stay unresolved.</summary>
        public RSHelpers.Profiles.ProfileIdentity[] experimentalProfileCatalog =
            Array.Empty<RSHelpers.Profiles.ProfileIdentity>();
    }
}
