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

        // ─────────────────────────────────────────────────────────────────────────
        // SONG-RUN CONTEXT (v0.6.5)
        //
        // The arrangement context (ID, path, tuning) of the song currently running.
        // Captured at LogSongStartIfPossible time and preserved through LogSongEnd.
        //
        // Why this exists: in Nonstop Play, the songID can flip to the NEXT song
        // before the CURRENT song's LogSongEnd has fired. The cross-reference logic
        // then nulls currentMemoryReadout.arrangementID (because the stale value no
        // longer matches the new song). If LogSongEnd reads from currentMemoryReadout
        // at that point, arrangementID is gone — and so is the right answer for
        // arrangement_path / arrangement_tuning if LogSongStartIfPossible never re-fires
        // for the next song (state machine parked in SONG_ENDING). These three fields
        // hold the original resolved values so end-of-song logging stays correct.
        // ─────────────────────────────────────────────────────────────────────────
        private string currentSongRunArrangementID = null;
        private string currentSongRunPath = null;
        private string currentSongRunTuning = null;

        // ─────────────────────────────────────────────────────────────────────────
        // FIRE-ONCE GUARDS (v0.6.5)
        //
        // Track which songID we last logged START / END for. The natural state-machine
        // path and the gameStage / songID-change escape hatches BOTH may try to fire
        // these events; these fields ensure each event fires at most once per song run.
        //
        // Reset to null on songID change (so a re-play of the same song produces a new
        // run with its own start/end pair).
        // ─────────────────────────────────────────────────────────────────────────
        private string lastLogStartedForSongID = null;
        private string lastLogEndedForSongID = null;

        // Previous gameStage observed (used to detect transitions, primarily for
        // Nonstop Play where the timer-based state machine is unreliable).
        private string lastGameStage = null;

        // ─────────────────────────────────────────────────────────────────────────
        // DEFERRED START LOGGING (v0.6.5)
        //
        // In Nonstop Play, the arrangement_hash memory pointer can lag the actual
        // song start by several polls — Rocksmith hasn't populated it yet by the
        // time the state machine wants to fire LogSongStart. Pre-deferral, this
        // resulted in path="unknown" / tuning="unknown" being logged for songs
        // with multiple playable arrangements (where the fallback heuristic can't
        // disambiguate). With deferral, we wait up to ~5 seconds for the memory
        // to populate before falling through to the unknown log path. Counter
        // ticks every poll; the retry hook in DoMemoryReadout calls
        // LogSongStartIfPossible repeatedly while we're in an in-game gameStage.
        // ─────────────────────────────────────────────────────────────────────────
        private int startLogDeferralCount = 0;
        private const int START_LOG_DEFERRAL_MAX = 50; // ~5s at 100ms polling

        // ─────────────────────────────────────────────────────────────────────────
        // PREV-PATH HEURISTIC (v0.6.5 hotfix)
        //
        // Persists the path (arrangement type — Lead/Rhythm/Bass) of the most recent
        // successfully-resolved song-start. Used as a fallback in LogSongStartIfPossible
        // when the arrangementID memory hasn't populated AND the song has multiple
        // playable arrangements (so the single-playable heuristic can't disambiguate).
        //
        // Mirrors the JS-side prevPath fallback in sniffer-poller.js's
        // getCurrentArrangement: a bassist who plays Bass on every song will keep
        // getting Bass auto-resolved on subsequent songs even when arrangementID
        // memory is slow to populate (typical for Nonstop Play).
        //
        // Initialized to null; populated on first successful resolution. Stays
        // populated across songs and Run() restarts within the same Sniffer instance.
        // ─────────────────────────────────────────────────────────────────────────
        private string lastResolvedPath = null;

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
        /// Persistent state — survives across sessions. Currently used to remember the
        /// most recently-resolved arrangement type, so the Nonstop fallback chain has
        /// a sensible answer even on the FIRST song of a fresh session (without this,
        /// in-session lastResolvedPath starts null and the first song always falls
        /// through to defaultArrangementType setting or "unknown").
        ///
        /// Type is named SnifferRuntimeState (not SnifferState) to avoid colliding
        /// with the existing SnifferState enum in this namespace, which represents
        /// the polling state machine (IN_MENUS / SONG_PLAYING / etc.).
        /// </summary>
        private readonly SnifferRuntimeState _state;

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

            // Load persistent state and seed in-session lastResolvedPath from it. This
            // means a returning user gets their previously-known arrangement type as the
            // initial fallback for the very first song of the new session, instead of
            // starting from null and falling back to defaultArrangementType / "unknown".
            _state = SnifferRuntimeState.Load();
            if (!string.IsNullOrEmpty(_state.LastResolvedPath))
            {
                lastResolvedPath = _state.LastResolvedPath;
            }

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
                    // ─────────────────────────────────────────────────────────────────
                    // FORCE-END OLD SONG (v0.6.5)
                    //
                    // If we previously fired LogSongStart for a song (lastLogStartedForSongID
                    // matches the OUTGOING currentCDLCDetails) but never fired LogSongEnd for
                    // it, force-fire end now — BEFORE currentCDLCDetails is updated to the new
                    // song. This is the primary fix for Nonstop Play, where the timer-based
                    // state machine can stay parked in SONG_ENDING and never naturally call
                    // LogSongEnd between songs.
                    //
                    // The completed flag is decided by a heuristic: if max observed timer
                    // reached close to song length, treat as completed; otherwise as quit.
                    // ─────────────────────────────────────────────────────────────────
                    if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                        lastLogStartedForSongID != null &&
                        lastLogStartedForSongID == currentCDLCDetails.songID &&
                        lastLogEndedForSongID != currentCDLCDetails.songID)
                    {
                        bool reachedEnd = (maxTime != float.MinValue) &&
                                          (maxTime >= currentCDLCDetails.songLength - 0.5f);
                        LogSongEnd(reachedEnd);

                        // Reset state machine and timing for the upcoming new song
                        currentState = SnifferState.IN_MENUS;
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        stallCount = 0;
                        lastObservedTimer = float.MinValue;
                        paused = false;
                    }

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

                        // Reset song-run context and fire-once guards for the new song
                        currentSongRunArrangementID = null;
                        currentSongRunPath = null;
                        currentSongRunTuning = null;
                        lastLogStartedForSongID = null;
                        lastLogEndedForSongID = null;
                        startLogDeferralCount = 0;
                    }

                }

                // ARRANGEMENT ID CROSS-REFERENCE (v0.6.5):
                //
                // The arrangement_hash memory pointer in RSMemoryReader can return a STALE valid
                // hash from the previous song after the songID has already flipped to the new
                // song — particularly in Nonstop Play, where the game transitions between songs
                // without fully clearing the arrangement memory region. Format validation in
                // RSMemoryReader (IsValidArrangementHash) cannot detect this because the stale
                // value is a real 32-char hex hash; it just belongs to the wrong song. The same
                // condition occurs whenever the user browses through songs in song-select after
                // playing one — every browsed songID has the just-played arrangementID in memory.
                //
                // At this point in the loop, currentCDLCDetails has been updated to reflect the
                // current songID, and its arrangements list is the authoritative set of valid
                // arrangement IDs for this song. If newReadout.arrangementID doesn't appear in
                // that list, it's stale — silently clear it. (The natural use case of this
                // clearing is browsing through songs after one was played, which is not a bug
                // and shouldn't produce log noise. Diagnostic visibility is preserved through
                // the LogSongStartIfPossible fallback warnings, which fire when the arrangement
                // can't be resolved AT THE MOMENT we're trying to log a song start — the only
                // moment when a missing arrangementID is actually a problem.)
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
                        newReadout.arrangementID = null;
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

                // ─────────────────────────────────────────────────────────────────────
                // GAME-STAGE TRANSITION DETECTION (v0.6.5) — primarily for Nonstop Play.
                //
                // The timer-based state machine in UpdateState() can be unreliable in
                // Nonstop, where the C# state can stay parked in SONG_ENDING between
                // songs (it only naturally exits on songTimer == 0, which doesn't always
                // happen between consecutive Nonstop songs). gameStage is a more direct
                // signal of what Rocksmith is actually doing.
                //
                // Known gameStage strings (Remastered):
                //   Learn-A-Song:  las_songs / las_options / las_tuner / las_game / las_songreview
                //   Score Attack:  gcpre / sa_game / sa_pause / sa_songreview
                //   Nonstop Play:  nsp_main / nonstopplaygame / nonstopplayhub
                //
                // Only the Nonstop transitions need this escape hatch — Learn-A-Song
                // and Score Attack work correctly under the existing timer-based logic.
                // ─────────────────────────────────────────────────────────────────────
                string currentGameStage = currentMemoryReadout.gameStage;
                if (currentGameStage != lastGameStage)
                {
                    string prevStage = lastGameStage;
                    string newStage = currentGameStage;

                    // nonstopplaygame → nonstopplayhub: current song just ended.
                    // Force-fire LogSongEnd if we have a started-but-not-ended song.
                    // Note: typically the songID-change force-end (above) catches this
                    // first; this is a backstop for when the gameStage transitions
                    // before the songID changes.
                    if (prevStage == "nonstopplaygame" && newStage == "nonstopplayhub")
                    {
                        if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                            lastLogStartedForSongID != null &&
                            lastLogStartedForSongID == currentCDLCDetails.songID &&
                            lastLogEndedForSongID != currentCDLCDetails.songID)
                        {
                            bool reachedEnd = (maxTime != float.MinValue) &&
                                              (maxTime >= currentCDLCDetails.songLength - 0.5f);
                            LogSongEnd(reachedEnd);

                            currentState = SnifferState.IN_MENUS;
                            lowTime = float.MaxValue;
                            initTime = float.MaxValue;
                            maxTime = float.MinValue;
                            stallCount = 0;
                            lastObservedTimer = float.MinValue;
                            paused = false;
                        }
                    }
                    // nonstopplayhub → nonstopplaygame: new song is now being played.
                    // Force-fire LogSongStartIfPossible if we haven't started this song.
                    //
                    // initTime guard (v0.6.5 hotfix3): only fire if the song timer has actually
                    // started advancing past initTime. songTimer briefly flashes nonzero values
                    // during loading screens — without this guard, gameStage transitioning to
                    // "nonstopplaygame" the moment the chart loads (before the user actually
                    // starts playing) would force-fire LogSongStart prematurely. Mirrors the
                    // natural state machine's SONG_STARTING → SONG_PLAYING guard.
                    else if (prevStage == "nonstopplayhub" && newStage == "nonstopplaygame")
                    {
                        if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                            lastLogStartedForSongID != currentCDLCDetails.songID &&
                            initTime != float.MaxValue &&
                            currentMemoryReadout.songTimer > initTime)
                        {
                            LogSongStartIfPossible();
                            // We're definitely in-game now; advance state machine accordingly.
                            currentState = SnifferState.SONG_PLAYING;
                        }
                    }

                    lastGameStage = newStage;
                }

                // ─────────────────────────────────────────────────────────────────────
                // DEFERRED-START RETRY (v0.6.5)
                //
                // LogSongStartIfPossible has its own fire-once guard AND deferral logic
                // — calling it repeatedly while we're in an in-game gameStage and start
                // hasn't been logged yet is safe and idempotent. This catches the case
                // where the natural state-machine path fired LogSongStartIfPossible too
                // early (before arrangement_hash memory had populated) and the call
                // returned without logging (deferred). On each subsequent poll we retry
                // until either the arrangement resolves or the deferral times out.
                //
                // las_game / sa_game / nonstopplaygame are the gameStage strings observed
                // for active gameplay in Remastered. Score Attack pause (sa_pause) is
                // intentionally excluded since the user has reported this stage can stick
                // — we don't want to keep retrying during a stuck pause.
                //
                // initTime guard (v0.6.5 hotfix3): same rationale as above — only retry
                // after the song timer has advanced past initTime so we don't fire start
                // during loading. The natural state machine sets initTime when songTimer
                // first goes nonzero; we wait until songTimer crosses lowTime + 0.101s.
                // ─────────────────────────────────────────────────────────────────────
                if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                    lastLogStartedForSongID != currentCDLCDetails.songID &&
                    initTime != float.MaxValue &&
                    currentMemoryReadout.songTimer > initTime)
                {
                    string gs = currentMemoryReadout.gameStage;
                    if (gs == "las_game" || gs == "sa_game" || gs == "nonstopplaygame")
                    {
                        LogSongStartIfPossible();
                    }
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

            // Fire-once guard: don't log start twice for the same song run.
            // Important when both the natural state machine AND the gameStage-transition
            // escape hatch try to fire start for the same song.
            if (lastLogStartedForSongID != null &&
                lastLogStartedForSongID == currentCDLCDetails.songID)
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
                    else if (playableCount > 1 && !string.IsNullOrEmpty(lastResolvedPath))
                    {
                        // PREV-PATH HEURISTIC (v0.6.5 hotfix):
                        // Multiple playable arrangements — try matching the last-resolved
                        // path from a previous song. This handles the Nonstop Play case
                        // where arrangement_hash memory hasn't populated yet AND the song
                        // has multiple playable arrangements (so the single-playable
                        // heuristic can't disambiguate). For users who consistently play
                        // one arrangement type (e.g. a bassist always plays Bass), this
                        // resolves correctly almost every time.
                        //
                        // lastResolvedPath is seeded at startup from SnifferRuntimeState
                        // (sniffer_state.json), so a returning user has their previous
                        // session's value pre-loaded for the first song of the new session.
                        // Within-session updates also persist to disk on each successful
                        // new resolution.
                        foreach (var arr in arrangements)
                        {
                            if (!arr.isBonusArrangement && !arr.isAlternateArrangement &&
                                (arr.type == lastResolvedPath || arr.name == lastResolvedPath))
                            {
                                arrangement = arr;
                                fallbackReason = "prev-path heuristic (\"" + lastResolvedPath + "\")";
                                break;
                            }
                        }
                    }

                    // DEFAULT ARRANGEMENT TYPE SETTING (v0.6.5 hotfix3):
                    // Multiple playable arrangements AND no prior resolution to fall back
                    // on (or prior resolution didn't match any of this song's arrangements).
                    // Try the user's configured default in SnifferSettings.defaultArrangementType.
                    // Lower priority than lastResolvedPath because the runtime-learned value
                    // reflects what the user actually plays; the setting is just a hint for
                    // first-ever-session bootstrap or for users whose most-played arrangement
                    // is more reliable than their most-recent.
                    if (arrangement == null && playableCount > 1 &&
                        _settings != null &&
                        !string.IsNullOrEmpty(_settings.defaultArrangementType))
                    {
                        string defaultType = _settings.defaultArrangementType;
                        foreach (var arr in arrangements)
                        {
                            if (!arr.isBonusArrangement && !arr.isAlternateArrangement &&
                                (arr.type == defaultType || arr.name == defaultType))
                            {
                                arrangement = arr;
                                fallbackReason = "defaultArrangementType setting (\"" + defaultType + "\")";
                                break;
                            }
                        }
                    }

                    if (arrangement == null && arrangements.Count == 1)
                    {
                        // Last resort: only one arrangement total (even if it's bonus/alternate, it's the only option)
                        arrangement = arrangements[0];
                        fallbackReason = "only-arrangement-on-song heuristic";
                    }
                }
            }

            // DEFERRAL (v0.6.5):
            //
            // If neither the arrangementID lookup nor the fallback heuristic resolved an
            // arrangement, defer logging up to ~5 seconds (50 polls) waiting for the
            // arrangement_hash memory pointer to populate. This is critical for Nonstop
            // Play, where Rocksmith updates arrangement_hash several polls AFTER gameStage
            // has already transitioned to "nonstopplaygame" — meaning early calls to this
            // function (from the state machine's SONG_PLAYING transition) would otherwise
            // log "unknown" path/tuning before the memory has caught up.
            //
            // Retry calls happen in DoMemoryReadout while the user is in an in-game stage
            // and start hasn't been logged yet. The fire-once guard above ensures we only
            // log once when we eventually succeed.
            //
            // If the deferral times out (~5s with no resolved arrangement), we fall through
            // and log with path="unknown" / tuning="unknown" so the song still gets a
            // history record (and the existing fallback warning fires for visibility).
            if (arrangement == null)
            {
                startLogDeferralCount++;
                if (startLogDeferralCount < START_LOG_DEFERRAL_MAX)
                {
                    return; // Try again on next call
                }
                // Deferral timed out — fall through to unknown logging
            }
            else
            {
                // Arrangement resolved — clear the counter
                startLogDeferralCount = 0;
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

            // Capture song-run context so end-of-song logging can recover even if
            // currentMemoryReadout.arrangementID is later cleared (Nonstop transition).
            string resolvedArrangementID = arrangement?.arrangementID;
            currentSongRunArrangementID = resolvedArrangementID;
            currentSongRunPath = path;
            currentSongRunTuning = tuning;

            // Track the resolved path for the prev-path fallback heuristic on future songs
            // (see fallback block above). This persists across songs so that, e.g., a bassist
            // who plays Bass on every song will keep getting Bass auto-resolved even when
            // arrangementID memory hasn't populated and the song has multiple playable
            // arrangements. Also persist to disk (sniffer_state.json) so a returning user
            // has the previous session's value pre-loaded for the first song of the next
            // session — without persistence, in-session lastResolvedPath would start null
            // every session and the first song would fall through to defaultArrangementType
            // / "unknown".
            if (!string.IsNullOrEmpty(path) && path != "unknown")
            {
                if (lastResolvedPath != path)
                {
                    lastResolvedPath = path;
                    // Save asynchronously to avoid blocking the polling loop on disk IO.
                    // SnifferState.Save() is best-effort and swallows IO errors internally.
                    if (_state != null)
                    {
                        _state.LastResolvedPath = path;
                        var stateToSave = _state;
                        System.Threading.Tasks.Task.Run(() => stateToSave.Save());
                    }
                }
            }

            // Fire-once guard: this songID's start is now logged.
            lastLogStartedForSongID = currentCDLCDetails.songID;

            // Reset the END guard (cleared from any previous run of THIS or any other song).
            // Without this clear, if the user replays the same song (songID unchanged), the
            // LogSongEnd fire-once check would see lastLogEndedForSongID == currentCDLCDetails.songID
            // from the previous run and silently skip the new run's end-event firing.
            lastLogEndedForSongID = null;

            // Fire event with actual gameplay start timestamp
            var actualStartTimestamp = DateTime.Now;
            OnActualSongStart?.Invoke(this, new OnActualSongStartArgs
            {
                song = currentCDLCDetails,
                timestamp = actualStartTimestamp,
                arrangementID = resolvedArrangementID,
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

            // Don't fire end if start wasn't fired for this song run (e.g. user
            // quit during the deferral window before LogSongStart succeeded).
            // Without this guard, end events without paired start events could
            // produce orphan rows in playthrough_history.
            if (lastLogStartedForSongID != currentCDLCDetails.songID)
            {
                return;
            }

            // Fire-once guard: don't log end twice for the same song run.
            // Important when both the natural state machine AND the songID-change /
            // gameStage-transition escape hatches try to fire end for the same song.
            if (lastLogEndedForSongID != null &&
                lastLogEndedForSongID == currentCDLCDetails.songID)
            {
                return;
            }

            // Snapshot the song details and readout NOW, so any later updates to
            // currentCDLCDetails / currentMemoryReadout don't bleed into the event payload.
            var snapshotSong = currentCDLCDetails;
            var snapshotReadout = currentMemoryReadout?.Clone();
            var noteData = snapshotReadout?.noteData ?? currentMemoryReadout.noteData;

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
            if (snapshotReadout != null && snapshotReadout.mode == RSMode.SCOREATTACK && noteData is ScoreAttackNoteData saData)
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

            // Mark this song's end as fired BEFORE invoking OnActualSongEnd
            // so re-entrant handlers (defensive) see the fire-once state.
            lastLogEndedForSongID = snapshotSong.songID;

            // Fire event with actual gameplay end timestamp.
            // Pass the song-run arrangement context (preserved from LogSongStart) and the
            // readout snapshot, so PlaythroughHistory can write the correct values even
            // if currentMemoryReadout / currentCDLCDetails have advanced to the next song.
            var actualEndTimestamp = DateTime.Now;
            OnActualSongEnd?.Invoke(this, new OnActualSongEndArgs
            {
                song = snapshotSong,
                timestamp = actualEndTimestamp,
                completed = completed,
                paused = paused,
                arrangementID = currentSongRunArrangementID,
                path = currentSongRunPath,
                tuning = currentSongRunTuning,
                readout = snapshotReadout
            });

            // Reset song-run state so a replay of the SAME song (songID unchanged: restart,
            // exit-and-replay, finish-and-replay) can fire start/end again as a NEW run.
            // We keep lastLogEndedForSongID set so any duplicate end-trigger paths in this
            // same poll cycle (e.g. songID-change AND gameStage-transition both trying to
            // force-end) get blocked; lastLogEndedForSongID is cleared on the next
            // successful LogSongStart.
            lastLogStartedForSongID = null;
            startLogDeferralCount = 0;
            currentSongRunArrangementID = null;
            currentSongRunPath = null;
            currentSongRunTuning = null;
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