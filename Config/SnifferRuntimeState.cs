using Newtonsoft.Json;
using System;
using System.IO;

namespace RockSnifferLib.Configuration
{
    /// <summary>
    /// Persistent state for the Sniffer that survives across RockSniffer sessions.
    /// Distinct from SnifferSettings: settings are user-edited preferences, state is
    /// values RockSniffer learns at runtime and wants to remember.
    ///
    /// Currently used for the Nonstop Play arrangement resolution fallback chain:
    /// LastResolvedPath holds the most recent arrangement type that was successfully
    /// resolved at song start. This seeds the cross-session fallback so that, after
    /// any successful resolution ever, we always have a sensible default — without
    /// requiring the user to configure anything.
    ///
    /// Stored as JSON in ./config/sniffer_state.json. Written on every successful
    /// resolution (write is cheap; file is tiny). Read once at sniffer startup.
    /// </summary>
    [Serializable]
    public class SnifferRuntimeState
    {
        /// <summary>
        /// The arrangement type ("Bass" / "Lead" / "Rhythm") most recently resolved
        /// at song start. null/empty means no resolution has occurred yet on this
        /// install — the fallback chain falls through to defaultArrangementType.
        /// </summary>
        public string LastResolvedPath { get; set; }

        // Default state file path — relative to working directory, alongside the
        // existing config files in ./config/.
        private const string DEFAULT_STATE_DIR = "./config/";
        private const string DEFAULT_STATE_FILE = "sniffer_state.json";
        private static string DefaultStatePath => DEFAULT_STATE_DIR + DEFAULT_STATE_FILE;

        /// <summary>
        /// Load state from the default location. Returns a new empty state if the
        /// file doesn't exist or is malformed; never throws.
        /// </summary>
        public static SnifferRuntimeState Load(string path = null)
        {
            string filePath = path ?? DefaultStatePath;
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var loaded = JsonConvert.DeserializeObject<SnifferRuntimeState>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
                // Malformed file or IO error — ignore and return fresh state.
            }
            return new SnifferRuntimeState();
        }

        /// <summary>
        /// Save state to the default location. Creates the config directory if it
        /// doesn't exist. Writes are best-effort: any IO error is swallowed (we
        /// don't want a save failure to kill a running session).
        /// </summary>
        public void Save(string path = null)
        {
            string filePath = path ?? DefaultStatePath;
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch
            {
                // Best-effort write — never let state save failure crash the app.
            }
        }
    }
}
