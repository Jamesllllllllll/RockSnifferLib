using RockSnifferLib.Cache;
using RockSnifferLib.Configuration;
using RockSnifferLib.Events;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.NoteData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace RockSnifferLib.Sniffing
{
    public class Sniffer
    {
        /// <summary>
        /// Fired when the Sniffer state has changed
        /// </summary>
        public event EventHandler<OnStateChangedArgs> OnStateChanged;

        /// <summary>
        /// Fired when the current song details have changed
        /// </summary>
        public event EventHandler<OnSongChangedArgs> OnSongChanged;

        /// <summary>
        /// Fired after each successful memory readout
        /// </summary>
        public event EventHandler<OnActualSongStartArgs> OnActualSongStart;
        public event EventHandler<OnActualSongEndArgs> OnActualSongEnd;
        public event EventHandler<OnMemoryReadoutArgs> OnMemoryReadout;

        /// <summary>
        /// Fired when a song starts
        /// </summary>
        public event EventHandler<OnSongStartedArgs> OnSongStarted;

        /// <summary>
        /// Fired when a song ends
        /// </summary>
        public event EventHandler<OnSongEndedArgs> OnSongEnded;

        /// <summary>
        /// Fired when a new psarc file is added to the dlc folder
        /// </summary>
        public event EventHandler<OnPsarcInstalledArgs> OnPsarcInstalled;

        /// <summary>
        /// The current state of rocksmith, initial state is IN_MENUS
        /// </summary>
        public SnifferState currentState = SnifferState.NONE;
        private SnifferState previousState = SnifferState.NONE;

        /// <summary>
        /// Currently active cdlc details
        /// </summary>
        private SongDetails currentCDLCDetails = new SongDetails();

        /// <summary>
        /// Currently active memory readout
        /// </summary>
        private RSMemoryReadout currentMemoryReadout = new RSMemoryReadout();

        /// <summary>
        /// Timer tracking for pause detection / game stage
        /// </summary>
        private float lowTime = float.MaxValue;
        private float initTime = float.MaxValue;
        private float maxTime = float.MinValue;
        private bool paused = false;
        private bool completed = false;

        /// <summary>
        /// Stall-counter pause detection: counts consecutive memory reads
        /// where the song timer has not meaningfully advanced.
        /// </summary>
        private float lastObservedTimer = float.MinValue;
        private int stallCount = 0;
        private const int STALL_THRESHOLD = 5;           // consecutive stalled reads to trigger pause
        private const float STALL_EPSILON = 0.001f;      // jitter tolerance (< 1 ms)
        private const float END_OF_SONG_PAUSE_GUARD = 2.0f; // suppress pause detection within last N seconds of song

        /// <summary>
        /// Suppressor for repeated stale-arrangementID warnings (added in v0.6.5).
        /// The cross-reference check fires every poll, so if the memory pointer keeps returning
        /// the same stale hash for many consecutive polls (e.g. through an entire Nonstop song
        /// transition), without this field we'd spam the log. This stores the most recent stale
        /// value we warned about; we only re-warn when the stale value changes or after a valid
        /// arrangement is observed (which clears this back to null).
        /// </summary>
        private string lastWarnedStaleArrangementID = null;

        // Public properties to expose completed and paused status
        public bool Completed => completed;
        public bool Paused => paused;

        /// <summary>
        /// Reference to the rocksmith process
        /// </summary>
        private readonly Process _rsProcess;

        /// <summary>
        /// Which _edition of Rocksmith we are attached to
        /// </summary>
        private readonly RSEdition _edition;

        /// <summary>
        /// Cache to use
        /// </summary>
        private readonly ICache _cache;

        /// <summary>
        /// The memory reader
        /// </summary>
        private readonly RSMemoryReader memReader;

        /// <summary>
        /// Settings this sniffer was instantiated with
        /// </summary>
        private readonly SnifferSettings _settings;

        /// <summary>
        /// Boolean to let async tasks finish
        /// </summary>
        private bool running = true;

        /// <summary>
        /// FileSystemWatchers to watch the dlc folder (and any symlinks)
        /// </summary>
        private List<FileSystemWatcher> fileSystemWatchers = new List<FileSystemWatcher>();

        /// <summary>
        /// An ActionBlock for processing psarc files
        /// </summary>
        private ActionBlock<string> psarcFileBlock;

        /// <summary>
        /// Instantiate a new Sniffer on process, using cache
        /// </summary>
        /// <param name="rsProcess"></param>
        /// <param name="cache"></param>
        /// <param name="edition"></param>
        /// <param name="settings"></param>
        public Sniffer(Process rsProcess, ICache cache, RSEdition edition, SnifferSettings? settings = null)
        {
            //Use default settings if no settings were given
            settings ??= new SnifferSettings();

            _rsProcess = rsProcess;
            _cache = cache;
            _edition = edition;
            _settings = settings;

            //Initialize memory reader
            memReader = new RSMemoryReader(_rsProcess, _edition);

            OnStateChanged += Sniffer_OnStateChanged;

            //Listen to PsarcInstalled event for auto enumeration
            if (settings.enableAutoEnumeration)
            {
                OnPsarcInstalled += Sniffer_OnPsarcInstalled;
            }

            DoMemoryReadout();
            DoStateMachine();
            DoSniffing();
        }

        /// <summary>
        /// Trigger enumeration when a new psarc file is installed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sniffer_OnPsarcInstalled(object sender, OnPsarcInstalledArgs e)
        {
            Logger.Log("New PSARC file installed: {0}", e.FilePath);
            TriggerEnumeration();
        }

        /// <summary>
        /// Trigger the enumerate flag, causing rocksmith to start enumerating
        /// </summary>
        public void TriggerEnumeration()
        {
            memReader.TriggerEnumeration();
        }

        /// <summary>
        /// Handle specific events based on state changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Sniffer_OnStateChanged(object sender, OnStateChangedArgs e)
        {
            var newState = e.newState;
            var oldState = e.oldState;

            if (oldState is SnifferState.IN_MENUS or SnifferState.SONG_SELECTED &&
                newState is SnifferState.SONG_STARTING or SnifferState.SONG_PLAYING)
            {
                OnSongStarted?.Invoke(this, new OnSongStartedArgs { song = currentCDLCDetails });
            }
            else if (newState == SnifferState.IN_MENUS &&
                oldState != SnifferState.NONE)
            {
                OnSongEnded?.Invoke(this, new OnSongEndedArgs { song = currentCDLCDetails, completed = completed, paused = paused });
            }
        }

        private async void DoMemoryReadout()
        {
            while (running)
            {
                await Task.Delay(100);

                RSMemoryReadout newReadout = null;

                try
                {
                    //Read data from memory
                    newReadout = memReader.DoReadout();
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while reading memory: {0} {1}\r\n{2}", e.GetType(), e.Message, e.StackTrace);
                    }
                }

                if (newReadout == null)
                {
                    continue;
                }

                if (newReadout.songID != currentMemoryReadout.songID || (currentCDLCDetails == null || !currentCDLCDetails.IsValid()))
                {
                    var newDetails = _cache.Get(newReadout.songID);

                    if (newDetails != null && newDetails.IsValid())
                    {
                        currentCDLCDetails = _cache.Get(newReadout.songID);
                        OnSongChanged?.Invoke(this, new OnSongChangedArgs { songDetails = currentCDLCDetails });
                        currentCDLCDetails.Print();

                        // Reset pause / timing state on song change
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        stallCount = 0;
                        lastObservedTimer = float.MinValue;
                        paused = false;

                        // Reset stale-arrangementID warning suppressor so each new song starts fresh
                        lastWarnedStaleArrangementID = null;
                    }

                }

                // ARRANGEMENT ID CROSS-REFERENCE (added in v0.6.5):
                //
                // The arrangement_hash memory pointer in RSMemoryReader can return a STALE valid
                // hash from the previous song after the songID has already flipped to the new
                // song — particularly in Nonstop Play, where the game transitions between songs
                // without fully clearing the arrangement memory region. Format validation in
                // RSMemoryReader (IsValidArrangementHash) cannot detect this because the stale
                // value is a real 32-char hex hash; it just belongs to the wrong song.
                //
                // At this point in the loop, currentCDLCDetails has been updated to reflect the
                // current songID, and its arrangements list is the authoritative set of valid
                // arrangement IDs for this song. If newReadout.arrangementID doesn't appear in
                // that list, it's stale (or somehow wrong) — clear it. This prevents downstream
                // consumers (the JS addon layer, EnoLogic event payloads, etc.) from receiving
                // an arrangementID that can't be matched against the current song's data.
                //
                // The next memory poll that returns a hash matching the new song will populate
                // arrangementID correctly. If it never does (pathological case), a fallback in
                // LogSongStartIfPossible() handles song-start logging gracefully.
                if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                    !string.IsNullOrEmpty(newReadout.arrangementID))
                {
                    bool matchesAnArrangement = false;
                    foreach (var arr in currentCDLCDetails.arrangements)
                    {
                        if (arr.arrangementID == newReadout.arrangementID)
                        {
                            matchesAnArrangement = true;
                            break;
                        }
                    }
                    if (!matchesAnArrangement)
                    {
                        // Suppress repeat warnings for the same stale value within a song to avoid log spam.
                        // Only log when the stale value is *new* relative to the previous poll's stale value.
                        if (newReadout.arrangementID != lastWarnedStaleArrangementID)
                        {
                            Logger.LogError(
                                "ArrangementID '{0}' from memory does not match any arrangement in current song '{1}' — clearing as stale (likely from a previous song or uninitialized memory).",
                                newReadout.arrangementID,
                                currentCDLCDetails.songID ?? newReadout.songID ?? "<unknown>");
                            lastWarnedStaleArrangementID = newReadout.arrangementID;
                        }
                        newReadout.arrangementID = null;
                    }
                    else
                    {
                        // We got a valid match — clear the suppressor so future stale values get logged.
                        lastWarnedStaleArrangementID = null;
                    }
                }

                newReadout.CopyTo(ref currentMemoryReadout);

                // Track timer behaviour for pause detection
                if (currentMemoryReadout.songTimer >= 0.001f)
                {
                    // Set initTime to the first valid timer value, plus one polling interval buffer
                    if (lowTime == float.MaxValue || currentMemoryReadout.songTimer < lowTime)
                    {
                        lowTime = currentMemoryReadout.songTimer;
                        initTime = currentMemoryReadout.songTimer + 0.101f; // ~100ms polling interval + offset for safe restart detection
                    }

                    // Update max observed timer
                    maxTime = Math.Max(maxTime, currentMemoryReadout.songTimer);

                    // Stall counter: if timer hasn't meaningfully advanced, increment; otherwise reset
                    if (Math.Abs(currentMemoryReadout.songTimer - lastObservedTimer) < STALL_EPSILON)
                    {
                        stallCount++;
                    }
                    else
                    {
                        stallCount = 0;
                    }
                    lastObservedTimer = currentMemoryReadout.songTimer;
                }

                OnMemoryReadout?.Invoke(this, new OnMemoryReadoutArgs() { memoryReadout = currentMemoryReadout });

                //Print memreadout if debug is enabled
                currentMemoryReadout.Print();
            }
        }

        private async void DoStateMachine()
        {
            while (running)
            {
                try
                {
                    //Update the state
                    UpdateState();
                }
                catch (Exception e)
                {
                    if (running)
                    {
                        Logger.LogError("Error while processing state machine: {0} {1}", e.GetType(), e.Message);
                    }
                }

                //Delay for 100 milliseconds
                await Task.Delay(100);
            }
        }

        private void CreateFileSystemWatcher(string path, string filter)
        {
            var watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = true,

                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,

                //Increase buffer size to 64k to avoid losing files
                InternalBufferSize = 1024 * 64
            };

            watcher.Created += PsarcFileChanged;
            watcher.Changed += PsarcFileChanged;
            watcher.Renamed += PsarcFileChanged;
            watcher.Error += Watcher_Error;

            watcher.EnableRaisingEvents = true;

            fileSystemWatchers.Add(watcher);

            Logger.Log("Created FileSystemWatcher for {0}", path);
        }

        private void FindSymLinks(string path, List<string> symlinks)
        {
            // Get all directories
            var dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);

            // Go through all found directories
            foreach (var dir in dirs)
            {
                // Check if path has the reparsepoint attribute (it is most likely a symlink)
                if (new FileInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Logger.Log($"Found symlink at {dir}");
                    symlinks.Add(dir);
                }
            }
        }

        private async void DoSniffing()
        {
            // Get path to rs directory
            var path = Path.GetDirectoryName(_rsProcess.MainModule.FileName);

            // Create main watcher for the dlc folder
            CreateFileSystemWatcher(path + Path.DirectorySeparatorChar + "dlc", "*.psarc");

            // Find all symbolic links and create a watcher for each
            var symlinks = new List<string>();
            FindSymLinks(path + Path.DirectorySeparatorChar + "dlc", symlinks);

            // Create a watcher for each symlink
            foreach (var symlink in symlinks) CreateFileSystemWatcher(symlink, "*.psarc");

            // Clamp to max 8 parallelism, because going higher is pretty ridiculous
            // Going higher is still possible manually through the config
            int parallelism = Math.Min(8, Math.Max(1, Environment.ProcessorCount));

            //Use parallelism value from settings
            if (_settings.parallelism > 0) parallelism = _settings.parallelism;

            Logger.Log("Using parallelism of {0}", parallelism);
            psarcFileBlock = new ActionBlock<string>(psarcFile => ProcessPsarcFile(psarcFile), new ExecutionDataflowBlockOptions() { MaxDegreeOfParallelism = parallelism });

            await Task.Run(() => ProcessAllPsarcs(path));
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            Logger.LogError("FileSystemWatcher Error: {0}", e.GetException().Message);
            Logger.LogException(e.GetException());
        }

        /// <summary>
        /// Queue to keep track of files that are due for parsing
        /// to avoid parsing the same file multiple times
        /// </summary>
        private static List<string> processingQueue = new List<string>();
        private void PsarcFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Logger.logProcessingQueue) Logger.Log("FileSystemWatcher: {0} \"{1}\"", e.ChangeType, e.Name);

            var psarcFile = e.FullPath;

            //Avoid duplicates in the block
            if (processingQueue.Contains(psarcFile)) return;

            processingQueue.Add(psarcFile);

            //Add to block to process the psarc file
            bool posted = psarcFileBlock.Post(psarcFile);

            //If post was not successful
            if (!posted) Logger.LogError("Unable to post {0} to psarcFileBlock", psarcFile);

            if (Logger.logProcessingQueue) Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);

        }

        private void PsarcFileProcessingDone(string psarcFile, bool success)
        {
            //If file was in the queue (triggered by filesystemwatcher)
            if (processingQueue.Contains(psarcFile))
            {
                //If processing was successful, invoke event
                OnPsarcInstalled?.Invoke(this, new OnPsarcInstalledArgs() { FilePath = psarcFile, ParseSuccess = success });

                //Remove from queue
                processingQueue.Remove(psarcFile);
            }

            if (Logger.logProcessingQueue)
            {
                Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);
            }
        }

        private void ProcessPsarcFile(string psarcFile)
        {
            var fileInfo = new FileInfo(psarcFile);

            // Try to hash the psarc file
            string hash;
            try
            {
                hash = PSARCUtil.GetFileHash(fileInfo);
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to calculate hash for {0}", psarcFile);
                Logger.LogException(e);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //Return if file is already cached
            if (_cache.Contains(psarcFile, hash))
            {
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //Read psarc data
            Dictionary<string, SongDetails> allSongDetails;
            try
            {
                allSongDetails = PSARCUtil.ReadPSARCHeaderData(fileInfo, hash);
            }
            catch (Exception e)
            {
                Logger.LogError("Unable to read {0}", psarcFile);
                Logger.LogException(e);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            //If loading was successful
            if (allSongDetails != null)
            {
                //In case file hash was different
                //or if this is a newer psarc with the same song ids
                //Remove all existing entries
                _cache.Remove(psarcFile, allSongDetails.Keys.ToList());

                //Add this CDLC file to the cache
                _cache.Add(psarcFile, allSongDetails);
            }

            PsarcFileProcessingDone(psarcFile, true);
        }

        private void ProcessAllPsarcs(string path)
        {
            //Build a list of all dlc psarc files, including songs.psarc
            List<string> psarcFiles = new List<string>
            {
                path + $"{Path.DirectorySeparatorChar}songs.psarc"
            };

            //Go into the dlc folder
            path += $"{Path.DirectorySeparatorChar}dlc";

            GetAllPsarcFiles(path, psarcFiles);

            foreach (string psarcFile in psarcFiles)
            {
                psarcFileBlock.Post(psarcFile);
            }

            Logger.Log("Found {0} psarc files", psarcFiles.Count);
        }

        private void GetAllPsarcFiles(string path, List<string> files)
        {
            //Add all files in the current path including all subdirectories
            files.AddRange(Directory.GetFiles(path, "*_p.psarc", SearchOption.AllDirectories));
        }

        /// <summary>
        /// Stops the sniffer, stopping all async tasks
        /// </summary>
        public void Stop()
        {
            running = false;

            foreach (var watcher in fileSystemWatchers)
            {
                watcher.Dispose();
            }

            fileSystemWatchers.Clear();
        }

        /// <summary>
        /// Update the state of the sniffer
        /// </summary>
        /// 
        private void LogSongStartIfPossible()
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return;
            }

            // Find arrangement by ID
            var arrangement = currentCDLCDetails.arrangements?
                .FirstOrDefault(a => a.arrangementID == currentMemoryReadout.arrangementID);

            // FALLBACK HEURISTIC (added in v0.6.5):
            //
            // If arrangementID lookup failed, attempt to recover so the song still gets logged
            // to playthrough history. Pre-v0.6.5, a missing/junk arrangementID caused this
            // function to silently early-return — meaning the song was never logged at all.
            //
            // Heuristic: if there's exactly one "playable" arrangement (non-bonus, non-alternate),
            // use that. This handles the most common case (single-arrangement songs and most
            // multi-arrangement songs where there's a clear "main" path). If there are multiple
            // playable arrangements or zero, we can't disambiguate — log the song with
            // path="unknown" / tuning="unknown" so the row still appears in history with
            // detectable markers.
            //
            // Rationale for "log unknown rather than skip": missing rows are harder to notice
            // than rows marked "unknown". The user can filter on path="unknown" later to find
            // affected sessions, or manually correct in their SQLite.
            string fallbackReason = null;
            if (arrangement == null)
            {
                var arrangements = currentCDLCDetails.arrangements;

                if (arrangements != null && arrangements.Count > 0)
                {
                    // Try to pick a single non-bonus, non-alternate arrangement
                    ArrangementDetails singlePlayable = null;
                    int playableCount = 0;
                    foreach (var arr in arrangements)
                    {
                        if (!arr.isBonusArrangement && !arr.isAlternateArrangement)
                        {
                            singlePlayable = arr;
                            playableCount++;
                            if (playableCount > 1) break; // can stop early — already ambiguous
                        }
                    }

                    if (playableCount == 1)
                    {
                        arrangement = singlePlayable;
                        fallbackReason = "single-playable-arrangement heuristic";
                    }
                    else if (arrangements.Count == 1)
                    {
                        // Last resort: only one arrangement total (even if it's bonus/alternate, it's the only option)
                        arrangement = arrangements[0];
                        fallbackReason = "only-arrangement-on-song heuristic";
                    }
                }
            }

            string path;
            string tuning;

            if (arrangement != null)
            {
                path = arrangement.type;
                tuning = arrangement.tuning.TuningName;

                if (fallbackReason != null)
                {
                    Logger.LogError(
                        "Could not resolve arrangement at song start (memory arrangementID was '{0}'). Used fallback ({1}) and chose path='{2}', tuning='{3}'. Song will be logged to history with these values.",
                        currentMemoryReadout.arrangementID ?? "<null>",
                        fallbackReason,
                        path,
                        tuning);
                }
            }
            else
            {
                // No usable arrangement and the heuristic couldn't disambiguate.
                // Log the song anyway with explicit "unknown" markers so the row isn't silently dropped.
                path = "unknown";
                tuning = "unknown";
                int arrCount = currentCDLCDetails.arrangements?.Count ?? 0;
                Logger.LogError(
                    "Could not resolve arrangement at song start for song '{0}' (memory arrangementID was '{1}'). Song has {2} arrangements but none could be unambiguously selected — logging to history with path='unknown' / tuning='unknown'.",
                    currentCDLCDetails.songID ?? "<unknown>",
                    currentMemoryReadout.arrangementID ?? "<null>",
                    arrCount);
            }

            Logger.Log(
                "EVENT=START;" +
                "artist=" + currentCDLCDetails.artistName + ";" +
                "album=" + currentCDLCDetails.albumName + ";" +
                "year=" + currentCDLCDetails.albumYear + ";" +
                "song=" + currentCDLCDetails.songName + ";" +
                "length=" + currentCDLCDetails.songLength + ";" +
                "path=" + path + ";" +
                "tuning=" + tuning + ";" +
                "author=" + (currentCDLCDetails.toolkit?.author ?? "").Trim() + ";"
            );

            // Fire event with actual gameplay start timestamp
            var actualStartTimestamp = DateTime.Now;
            OnActualSongStart?.Invoke(this, new OnActualSongStartArgs
            {
                song = currentCDLCDetails,
                timestamp = actualStartTimestamp,
                path = path,
                tuning = tuning
            });
        }

        private void LogSongEnd(bool completed)
        {
            if (currentCDLCDetails == null || !currentCDLCDetails.IsValid())
            {
                return;
            }

            var noteData = currentMemoryReadout.noteData;

            // Build base log message
            StringBuilder logMessage = new StringBuilder();
            logMessage.Append("EVENT=END;");
            logMessage.Append($"completed={completed};");
            logMessage.Append($"paused={paused};");
            logMessage.Append($"accuracy={Math.Round(noteData.Accuracy, 1)}%;");
            logMessage.Append($"totalNotes={noteData.TotalNotes};");
            logMessage.Append($"notesHit={noteData.TotalNotesHit};");
            logMessage.Append($"highestStreak={noteData.HighestHitStreak};");

            // Add Score Attack specific stats if in Score Attack mode
            if (currentMemoryReadout.mode == RSMode.SCOREATTACK && noteData is ScoreAttackNoteData saData)
            {
                logMessage.Append($"Mode=true;");
                logMessage.Append($"TotalPerfectHits={saData.TotalPerfectHits};");
                logMessage.Append($"PerfectPhrases={saData.PerfectPhrases};");
                logMessage.Append($"GoodPhrases={saData.GoodPhrases};");
                logMessage.Append($"PassedPhrases={saData.PassedPhrases};");
                logMessage.Append($"FailedPhrases={saData.FailedPhrases};");
                logMessage.Append($"HighestPerfectPhraseStreak={saData.HighestPerfectPhraseStreak};");
                logMessage.Append($"HighestGoodPhraseStreak={saData.HighestGoodPhraseStreak};");
                logMessage.Append($"HighestPassedPhraseStreak={saData.HighestPassedPhraseStreak};");
                logMessage.Append($"HighestFailedPhraseStreak={saData.HighestFailedPhraseStreak};");
                logMessage.Append($"CurrentScore={saData.CurrentScore};");
                logMessage.Append($"HighestMultiplier={saData.HighestMultiplier};");
            }

            Logger.Log(logMessage.ToString());

            // Fire event with actual gameplay end timestamp
            var actualEndTimestamp = DateTime.Now;
            OnActualSongEnd?.Invoke(this, new OnActualSongEndArgs
            {
                song = currentCDLCDetails,
                timestamp = actualEndTimestamp,
                completed = completed,
                paused = paused
            });
        }

        /// <summary>
        /// Update the state of the sniffer
        /// </summary>
        private void UpdateState()
        {
            // Super complex state machine of state transitions
            switch (currentState)
            {
                case SnifferState.IN_MENUS:
                    if (currentMemoryReadout.songTimer != 0)
                    {
                        currentState = SnifferState.SONG_SELECTED;
                    }
                    break;

                case SnifferState.SONG_SELECTED:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        currentState = SnifferState.SONG_STARTING;
                    }

                    // If we somehow missed some states, skip to SONG_PLAYING
                    // Using initTime instead of a hard-coded 1s threshold
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        LogSongStartIfPossible();
                    }
                    break;

                case SnifferState.SONG_STARTING:
                    if (initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        LogSongStartIfPossible();
                    }
                    break;

                case SnifferState.SONG_PLAYING:
                    // Allow small margin at end of song
                    if (currentCDLCDetails != null &&
                        currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - 0.201f)
                    {
                        currentState = SnifferState.SONG_ENDING;
                    }

                    // If the timer goes to 0 without reaching the end, user quit / restarted
                    if (currentMemoryReadout.songTimer == 0 &&
                        initTime != float.MaxValue)
                    {
                        // Early quit, not completed
                        completed = false;

                        LogSongEnd(completed: false);
                        currentState = SnifferState.IN_MENUS;

                        // Reset pause tracking for next run
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        stallCount = 0;
                        lastObservedTimer = float.MinValue;
                        paused = false;
                        break;
                    }

                    // If the song timer has stalled for enough consecutive reads, the user must have paused
                    // Suppress near end of song � the game engine often stalls the timer briefly
                    // during end-of-song transition (score screen prep) before reaching songLength
                    if (stallCount >= STALL_THRESHOLD &&
                        currentMemoryReadout.songTimer > initTime &&
                        !(currentCDLCDetails != null &&
                          currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - END_OF_SONG_PAUSE_GUARD))
                    {
                        currentState = SnifferState.SONG_PAUSED;
                        Logger.Log("Song Paused! (timer stalled at {0:F3} for {1} reads)", currentMemoryReadout.songTimer, stallCount);
                        paused = true;
                    }
                    break;

                case SnifferState.SONG_PAUSED:
                    // If the timer drops back to (or below) initTime, treat as restart / quit
                    if (currentMemoryReadout.songTimer <= initTime &&
                        initTime != float.MaxValue)
                    {
                        currentState = SnifferState.IN_MENUS;

                        // Not a full completion
                        completed = false;

                        LogSongEnd(completed: false);

                        // Reset timers so a new run gets clean values
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        stallCount = 0;
                        lastObservedTimer = float.MinValue;
                        paused = false;
                    }
                    // If songTimer is advancing again (stall counter reset), user has resumed
                    else if (stallCount == 0 && currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        Logger.Log("Song Resumed!");
                    }
                    break;

                case SnifferState.SONG_ENDING:
                    if (currentMemoryReadout.songTimer == 0)
                    {
                        // Completed run

                        completed = true;

                        LogSongEnd(completed: true);
                        currentState = SnifferState.IN_MENUS;

                        // Reset pause / timing
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        stallCount = 0;
                        lastObservedTimer = float.MinValue;
                        paused = false;
                    }
                    break;

                default:
                    break;
            }

            // Force state to IN_MENUS if the current song details are not valid
            if (!currentCDLCDetails.IsValid() &&
                currentState != SnifferState.IN_MENUS &&
                currentState != SnifferState.SONG_ENDING &&
                currentState != SnifferState.SONG_PAUSED)
            {
                currentState = SnifferState.IN_MENUS;
            }

            // If state changed, fire the event (this is what RockSniffer.exe / addons rely on)
            if (currentState != previousState)
            {
                OnStateChanged?.Invoke(this, new OnStateChangedArgs()
                {
                    oldState = previousState,
                    newState = currentState
                });

                previousState = currentState;

                if (Logger.logStateMachine)
                {
                    Logger.Log("Current state: {0}", currentState.ToString());
                }
            }
        }
    }
}