using System;

namespace RockSnifferLib.Configuration
{
    [Serializable]
    public class SnifferSettings
    {
        public bool enableAutoEnumeration = true;
        public int parallelism = 0;
        /// <summary>Opt-in initial profile selection/login observation for the tested build.</summary>
        public bool enableExperimentalProfiles = false;
        /// <summary>Host-supplied saved profile identities; unknown/duplicate names stay unresolved.</summary>
        public RSHelpers.Profiles.ProfileIdentity[] experimentalProfileCatalog =
            Array.Empty<RSHelpers.Profiles.ProfileIdentity>();
    }
}
