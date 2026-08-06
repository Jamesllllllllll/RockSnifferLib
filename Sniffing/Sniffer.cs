using RockSnifferLib.Cache;
using RockSnifferLib.Configuration;
using RockSnifferLib.Events;
using RockSnifferLib.Logging;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.NoteData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        // stallCount and STALL_THRESHOLD removed in v0.6.7 — pause entry/exit now
        // flag-driven via RSMemoryReadout.pauseMenuMode (see
        // MemoryOffsets.GetPauseMenuModePointer). STALL_EPSILON and
        // END_OF_SONG_PAUSE_GUARD likewise removed; the flag is authoritative
        // regardless of timer position so end-of-song stall false-positives
        // can't happen. lastObservedTimer is retained for diagnostic logging
        // purposes only — no logic branches on it post-migration.

        /// <summary>
        /// Snapshot of songTimer at the moment SONG_PAUSED was entered (v0.6.7).
        ///
        /// Used by the SONG_PAUSED → SONG_PLAYING resume detection to
        /// distinguish "user resumed the song" (timer moves) from "user
        /// hit Exit from the pause menu" (timer stays frozen at the
        /// pause-point until the engine transitions back to menus).
        ///
        /// Without this disambiguation, the moment the user hits Exit
        /// from the pause menu, pauseMenuMode flips to 0 (engine clears
        /// the overlay) while songTimer is still frozen above initTime —
        /// causing a brief spurious SONG_PAUSED → SONG_PLAYING flicker
        /// before the songTimer-drops-to-0 branch fires SONG_PAUSED →
        /// IN_MENUS one or two polls later.
        ///
        /// Resume is now detected on `songTimer != pauseTimerSnapshot`
        /// (any movement away from the pause point — Rocksmith may
        /// rewind ~0.5s on resume for "catch up", so != is correct
        /// rather than >). Real resume → timer moves → resume fires
        /// on the next poll. Exit-from-pause → timer stays frozen
        /// until menu transition → resume never fires → the existing
        /// songTimer <= initTime branch catches the IN_MENUS transition.
        /// </summary>
        private float pauseTimerSnapshot = float.MinValue;

        /// <summary>
        /// Previous poll's pauseMenuMode value (v0.6.9). Pause-entry is detected on
        /// the 0 → non-zero TRANSITION of pauseMenuMode, not on the raw current
        /// value, so stale carry-over reads (e.g. immediately after the user
        /// clicks Restart from the pause menu — Rocksmith's pauseMenuMode briefly
        /// keeps reading 2 even though the new song has begun playing) don't
        /// trigger a false SONG_PLAYING → SONG_PAUSED transition.
        ///
        /// Captured at the start of every poll, before newReadout overwrites
        /// currentMemoryReadout. The pause-entry condition then requires
        /// `previousPauseMenuMode == 0 && currentMemoryReadout.pauseMenuMode != 0`.
        /// </summary>
        private byte previousPauseMenuMode = 0;

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
        // True if the current song run was started while in a Nonstop Play gameStage.
        // Preserved through end-of-song (Nonstop transitions can change gameStage between
        // start and end) so PlaythroughHistory and the JS playthrough-tracker can
        // consistently gate writes for the entire run regardless of when end fires.
        // (v0.6.8) No longer gates playthrough writes — that gate was lifted once
        // PLAY_arrID made Nonstop arrangement resolution reliable. Field preserved
        // for any downstream consumer that wants the contextual flag.
        private bool currentSongRunWasNonstopMode = false;

        // (v0.6.8) True if the current song run was started while in a Multiplayer
        // gameStage (RSMode.MULTIPLAYER — split_game, mp_*, duet_*, h2h_*). Used
        // by PlaythroughHistory and the JS playthrough-tracker to skip writes —
        // multi-user note data and per-user arrangements aren't tracked yet, so
        // MP rows would have data-quality issues. Captured at song start using the
        // v0.6.8 gameStage-derived mode field (more reliable than gameStage prefix
        // matching across menu / transition states).
        private bool currentSongRunWasMultiplayerMode = false;

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

        // The deferral fields (startLogDeferralCount, START_LOG_DEFERRAL_MAX) were
        // REMOVED in v0.6.5 hotfix5 along with the deferral block in
        // LogSongStartIfPossible and the retry hook in DoMemoryReadout. Their original
        // purpose was to wait for arrangement_hash memory to populate; with Path
        // resolution as the new primary mechanism, there's nothing to wait for — Path
        // is available from Rocksmith launch onward.
        //
        // The lastResolvedPath field, SnifferRuntimeState persistence, and
        // defaultArrangementType setting were also REMOVED in v0.6.5 cleanup.
        // Pre-Path, those were the working fallback chain when arrangementID failed.
        // Post-Path, they were unreachable in normal operation — Path resolution
        // (read from a stable memory byte) handles every case they used to handle.

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
        /// Synchronizes privacy-safe runtime diagnostics.
        /// </summary>
        private readonly object diagnosticsSync = new object();
        private long memoryReadAttempts = 0;
        private long successfulMemoryReads = 0;
        private long failedMemoryReads = 0;
        private string lastMemoryReadErrorType = null;
        private int catalogFilesDiscovered = 0;
        private int catalogFilesProcessed = 0;
        private int catalogFilesFailed = 0;
        private int catalogSongsLoaded = 0;
        private bool catalogScanComplete = false;
        private bool selectedSongDetected = false;
        private bool selectedSongResolved = false;

        /// <summary>
        /// Boolean to let async tasks finish
        /// </summary>
        private bool running = true;

        /// <summary>
        /// FileSystemWatchers to watch the dlc folder (and any symlinks)
        /// </summary>
        private List<FileSystemWatcher> fileSystemWatchers = new List<FileSystemWatcher>();
        private readonly object fileSystemWatcherSync = new object();

        /// <summary>
        /// An ActionBlock for processing psarc files
        /// </summary>
        private ActionBlock<string> psarcFileBlock;
        private readonly CancellationTokenSource sniffingCancellation =
            new CancellationTokenSource();

        /// <summary>
        /// Tracks file-system events for this Sniffer instance only. A replacement
        /// Sniffer must not inherit queued paths from a stopped reader.
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> processingQueue =
            new ConcurrentDictionary<string, byte>();

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
        /// Returns aggregate runtime health without file paths, song identifiers,
        /// memory values, or package contents.
        /// </summary>
        public SnifferDiagnosticsSnapshot GetDiagnostics()
        {
            lock (diagnosticsSync)
            {
                return new SnifferDiagnosticsSnapshot
                {
                    memoryReadAttempts = memoryReadAttempts,
                    successfulMemoryReads = successfulMemoryReads,
                    failedMemoryReads = failedMemoryReads,
                    lastMemoryReadErrorType = lastMemoryReadErrorType,
                    catalogFilesDiscovered = catalogFilesDiscovered,
                    catalogFilesProcessed = catalogFilesProcessed,
                    catalogFilesFailed = catalogFilesFailed,
                    catalogSongsLoaded = catalogSongsLoaded,
                    catalogScanComplete = catalogScanComplete,
                    selectedSongDetected = selectedSongDetected,
                    selectedSongResolved = selectedSongResolved,
                };
            }
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

                lock (diagnosticsSync)
                {
                    memoryReadAttempts++;
                }

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
                        lock (diagnosticsSync)
                        {
                            failedMemoryReads++;
                            lastMemoryReadErrorType = e.GetType().Name;
                        }
                    }
                }

                if (newReadout == null)
                {
                    continue;
                }

                lock (diagnosticsSync)
                {
                    successfulMemoryReads++;
                }

                bool selectedSongChanged = !string.Equals(
                    newReadout.songID,
                    currentMemoryReadout.songID,
                    StringComparison.OrdinalIgnoreCase
                );
                if (selectedSongChanged || (currentCDLCDetails == null || !currentCDLCDetails.IsValid()))
                {
                    // ─────────────────────────────────────────────────────────────────
                    // FORCE-END OLD SONG (v0.6.5; v0.6.9 completion-flag rewrite)
                    //
                    // If we previously fired LogSongStart for a song (lastLogStartedForSongID
                    // matches the OUTGOING currentCDLCDetails) but never fired LogSongEnd for
                    // it, force-fire end now — BEFORE currentCDLCDetails is updated to the new
                    // song. This is the primary fix for Nonstop Play, where the timer-based
                    // state machine can stay parked in SONG_ENDING and never naturally call
                    // LogSongEnd between songs.
                    //
                    // The completed flag is now determined by DetermineCompletedForForceEnd()
                    // using three deterministic signals (pause-state, SA-fail, no-progress)
                    // rather than the pre-v0.6.9 maxTime-near-songLength heuristic, which
                    // was vulnerable to poll-cadence races at song end.
                    // ─────────────────────────────────────────────────────────────────
                    if (currentCDLCDetails != null && currentCDLCDetails.IsValid() &&
                        lastLogStartedForSongID != null &&
                        lastLogStartedForSongID == currentCDLCDetails.songID &&
                        lastLogEndedForSongID != currentCDLCDetails.songID)
                    {
                        LogSongEnd(DetermineCompletedForForceEnd());

                        // Reset state machine and timing for the upcoming new song
                        currentState = SnifferState.IN_MENUS;
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                    }

                    var cachedDetails = _cache.Get(newReadout.songID);
                    var newDetails = SongSelectionGuard.ResolveDetails(
                        newReadout.songID,
                        currentCDLCDetails,
                        cachedDetails
                    );

                    if (!ReferenceEquals(newDetails, currentCDLCDetails))
                    {
                        currentCDLCDetails = newDetails;
                        OnSongChanged?.Invoke(this, new OnSongChangedArgs { songDetails = currentCDLCDetails });
                        if (currentCDLCDetails.IsValid())
                        {
                            currentCDLCDetails.Print();
                        }
                    }

                    if (selectedSongChanged)
                    {
                        // Reset pause / timing state on song change
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;

                        // Reset song-run context and fire-once guards for the new song
                        currentSongRunArrangementID = null;
                        currentSongRunPath = null;
                        currentSongRunTuning = null;
                        currentSongRunWasNonstopMode = false;
                        currentSongRunWasMultiplayerMode = false;
                        lastLogStartedForSongID = null;
                        lastLogEndedForSongID = null;
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

                // (v0.6.9) Capture previous poll's pauseMenuMode BEFORE CopyTo
                // overwrites currentMemoryReadout. The pause-entry condition below
                // requires prev==0 && curr!=0 (a real 0→non-zero transition) to
                // avoid firing on stale carry-over reads after a restart.
                previousPauseMenuMode = currentMemoryReadout?.pauseMenuMode ?? 0;

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

                    // lastObservedTimer kept updated for diagnostic logging only (v0.6.7);
                    // no state-machine logic branches on it post-migration to flag-driven pause.
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
                // Known gameStage strings (Remastered, post-v0.6.6 static-address reader):
                //   Learn-A-Song:  las_songs / las_options / las_tuner / las_game /
                //                  las_pause / las_songreview
                //   Score Attack:  gcpre / sa_game / sa_pause / sa_songreview
                //   Nonstop Play:  nsp_main / nonstopplaygame / nonstopplayhub /
                //                  nsp_pause / nsp_tuner
                //   Other:         main / panel_bib / shop / gc_games / mp_* / etc.
                //
                // Note (v0.6.6): *_pause stages are now visible across all three modes,
                // but Rocksmith does NOT update gameStage on pause→resume / pause→restart,
                // so a stale "*_pause" reading does not imply the user is still paused.
                // Use SnifferState (game_state) for actual play/pause status — it derives
                // from songTimer behavior, not gameStage strings.
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
                            LogSongEnd(DetermineCompletedForForceEnd());

                            currentState = SnifferState.IN_MENUS;
                            lowTime = float.MaxValue;
                            initTime = float.MaxValue;
                            maxTime = float.MinValue;
                                lastObservedTimer = float.MinValue;
                                pauseTimerSnapshot = float.MinValue;
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

                // The deferred-start retry hook (v0.6.5) was REMOVED in hotfix5.
                // Original purpose: retry LogSongStartIfPossible every poll while in an
                // in-game gameStage, in case the natural state machine fired it too
                // early (before arrangement_hash memory had populated) and it returned
                // deferred. With Path resolution (hotfix5) replacing arrangement_hash as
                // the primary resolution mechanism, deferral itself is gone — Path is
                // available from Rocksmith launch onward. The natural state machine and
                // the nonstopplayhub→nonstopplaygame transition handler each call
                // LogSongStartIfPossible exactly once per song, and that's sufficient.

                OnMemoryReadout?.Invoke(this, new OnMemoryReadoutArgs() { memoryReadout = currentMemoryReadout });

                lock (diagnosticsSync)
                {
                    selectedSongDetected = !string.IsNullOrWhiteSpace(currentMemoryReadout.songID);
                    selectedSongResolved = SongSelectionGuard.MatchesSelectedSong(
                        currentCDLCDetails,
                        currentMemoryReadout.songID
                    );
                }

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

            lock (fileSystemWatcherSync)
            {
                if (!running || sniffingCancellation.IsCancellationRequested)
                {
                    watcher.Dispose();
                    return;
                }

                watcher.EnableRaisingEvents = true;
                fileSystemWatchers.Add(watcher);
            }

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
            var cancellationToken = sniffingCancellation.Token;

            // Get path to rs directory
            var path = Path.GetDirectoryName(_rsProcess.MainModule.FileName);

            // Clamp to max 8 parallelism, because going higher is pretty ridiculous
            // Going higher is still possible manually through the config
            int parallelism = Math.Min(8, Math.Max(1, Environment.ProcessorCount));

            //Use parallelism value from settings
            if (_settings.parallelism > 0) parallelism = _settings.parallelism;

            Logger.Log("Using parallelism of {0}", parallelism);
            psarcFileBlock = new ActionBlock<string>(
                psarcFile => ProcessPsarcFile(psarcFile, cancellationToken),
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken,
                }
            );

            if (cancellationToken.IsCancellationRequested) return;

            // Create main watcher for the dlc folder
            CreateFileSystemWatcher(path + Path.DirectorySeparatorChar + "dlc", "*.psarc");

            // Find all symbolic links and create a watcher for each
            var symlinks = new List<string>();
            FindSymLinks(path + Path.DirectorySeparatorChar + "dlc", symlinks);

            // Create a watcher for each symlink
            foreach (var symlink in symlinks) CreateFileSystemWatcher(symlink, "*.psarc");

            try
            {
                await Task.Run(
                    () => ProcessAllPsarcs(path, cancellationToken),
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopping a Sniffer intentionally abandons its queued catalog scan.
            }
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
        private void PsarcFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!running || sniffingCancellation.IsCancellationRequested) return;

            if (Logger.logProcessingQueue) Logger.Log("FileSystemWatcher: {0} \"{1}\"", e.ChangeType, e.Name);

            var psarcFile = e.FullPath;

            //Avoid duplicates in the block
            if (!processingQueue.TryAdd(psarcFile, 0)) return;

            RecordCatalogFilesDiscovered(1);

            //Add to block to process the psarc file
            bool posted = psarcFileBlock.Post(psarcFile);

            //If post was not successful
            if (!posted)
            {
                processingQueue.TryRemove(psarcFile, out _);
                RecordCatalogFileProcessed(false, 0);
                Logger.LogError("Unable to post {0} to psarcFileBlock", psarcFile);
            }

            if (Logger.logProcessingQueue) Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);

        }

        private void PsarcFileProcessingDone(string psarcFile, bool success)
        {
            //If file was in the queue (triggered by filesystemwatcher)
            if (
                processingQueue.TryRemove(psarcFile, out _) &&
                running &&
                !sniffingCancellation.IsCancellationRequested
            )
            {
                //If processing was successful, invoke event
                OnPsarcInstalled?.Invoke(this, new OnPsarcInstalledArgs() { FilePath = psarcFile, ParseSuccess = success });
            }

            if (Logger.logProcessingQueue)
            {
                Logger.Log("Queue:{0} / Block:{1}", processingQueue.Count, psarcFileBlock.InputCount);
            }
        }

        private void RecordCatalogFilesDiscovered(int count)
        {
            lock (diagnosticsSync)
            {
                catalogFilesDiscovered += count;
                catalogScanComplete = false;
            }
        }

        private void RecordCatalogFileProcessed(bool success, int songsLoaded)
        {
            lock (diagnosticsSync)
            {
                catalogFilesProcessed++;
                if (!success)
                {
                    catalogFilesFailed++;
                }
                catalogSongsLoaded += Math.Max(0, songsLoaded);
                catalogScanComplete =
                    catalogFilesDiscovered > 0 &&
                    catalogFilesProcessed >= catalogFilesDiscovered;
            }
        }

        private void ProcessPsarcFile(
            string psarcFile,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
                return;
            }

            var fileInfo = new FileInfo(psarcFile);

            var isFileSystemEvent = processingQueue.ContainsKey(psarcFile);
            PSARCUtil.PSARCFileSnapshot readySnapshot;
            var isReady = isFileSystemEvent
                ? PSARCUtil.TryWaitForStablePSARC(
                    fileInfo,
                    out readySnapshot,
                    cancellationToken: cancellationToken
                )
                : PSARCUtil.TryGetPSARCFileSnapshot(fileInfo, out readySnapshot);

            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
                return;
            }

            if (!isReady)
            {
                Logger.Log(
                    "Skipping PSARC file until it is complete and stable: {0}",
                    psarcFile
                );
                RecordCatalogFileProcessed(false, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

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
                RecordCatalogFileProcessed(false, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
                return;
            }

            if (!PSARCUtil.MatchesPSARCFileSnapshot(fileInfo, readySnapshot))
            {
                Logger.Log(
                    "PSARC file changed while it was being inspected; waiting for another file event: {0}",
                    psarcFile
                );
                RecordCatalogFileProcessed(false, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
                return;
            }

            //Return if file is already cached
            if (_cache.Contains(psarcFile, hash))
            {
                RecordCatalogFileProcessed(true, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            if (!PSARCUtil.MatchesPSARCFileSnapshot(fileInfo, readySnapshot))
            {
                Logger.Log(
                    "PSARC file changed before parsing; waiting for another file event: {0}",
                    psarcFile
                );
                RecordCatalogFileProcessed(false, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
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
                RecordCatalogFileProcessed(false, 0);
                PsarcFileProcessingDone(psarcFile, false);
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                processingQueue.TryRemove(psarcFile, out _);
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

            RecordCatalogFileProcessed(true, allSongDetails?.Count ?? 0);
            PsarcFileProcessingDone(psarcFile, true);
        }

        private void ProcessAllPsarcs(
            string path,
            CancellationToken cancellationToken
        )
        {
            //Build a list of all dlc psarc files, including songs.psarc
            List<string> psarcFiles = new List<string>
            {
                path + $"{Path.DirectorySeparatorChar}songs.psarc"
            };

            //Go into the dlc folder
            path += $"{Path.DirectorySeparatorChar}dlc";

            GetAllPsarcFiles(path, psarcFiles);
            if (cancellationToken.IsCancellationRequested) return;

            RecordCatalogFilesDiscovered(psarcFiles.Count);

            foreach (string psarcFile in psarcFiles)
            {
                if (cancellationToken.IsCancellationRequested) return;

                if (!psarcFileBlock.Post(psarcFile))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    RecordCatalogFileProcessed(false, 0);
                }
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
            if (!running) return;

            running = false;
            sniffingCancellation.Cancel();

            lock (fileSystemWatcherSync)
            {
                foreach (var watcher in fileSystemWatchers)
                {
                    watcher.Dispose();
                }

                fileSystemWatchers.Clear();
            }

            processingQueue.Clear();
        }

        /// <summary>
        /// Update the state of the sniffer
        /// </summary>
        /// 
        private void LogSongStartIfPossible()
        {
            if (!SongSelectionGuard.MatchesSelectedSong(
                currentCDLCDetails,
                currentMemoryReadout?.songID
            ))
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

            // STEP 1: Direct arrangementID match (v0.6.5)
            // Best resolution — exact match. Works in LaS/SA when the arrangement_hash
            // memory pointer has populated. Fails in Nonstop Play (pointer doesn't
            // populate there at all).
            var arrangement = currentCDLCDetails.arrangements?
                .FirstOrDefault(a => a.arrangementID == currentMemoryReadout.arrangementID);

            string fallbackReason = null;

            // STEP 2: Current Path filter (v0.6.5 hotfix5)
            //
            // If direct arrangementID match failed, use the user's currently-selected
            // Path (read from a stable byte pointer at the menu level — see
            // MemoryOffsets.GetCurrentPathPointer for details). Path is reliable from
            // Rocksmith launch onward and works in Nonstop Play, where arrangement_hash
            // fails. It only encodes the path TYPE (Lead/Rhythm/Bass), not the specific
            // arrangement, so we still need to filter for non-bonus/non-alternate to
            // disambiguate when multiple arrangements share the same path type.
            //
            // Three sub-steps:
            //   2a) Path-type + non-bonus + non-alternate → if exactly one match, use it.
            //       This is the common case: most songs have one regular Bass / one
            //       regular Lead / one regular Rhythm. Bonus/alternate filtering rules
            //       them out so we land on the user's actual choice.
            //   2b) Path-type, bonus/alt allowed → if exactly one match, use it.
            //       Last resort within Path resolution. If the song has only a bonus
            //       Lead and no regular Lead, and the user has Path=Lead, we pick the
            //       bonus Lead — there's no other Lead option.
            //
            // Caveat: when bonus/alternate arrangements ARE enabled in Nonstop Play,
            // a song can have a regular Bass AND a bonus Bass. Path=Bass matches both;
            // we pick the regular one (2a). If the user is actually playing the bonus,
            // we silently mismatch. This is the bonus-ambiguity problem that keeps the
            // Nonstop Play playthrough_history / playthrough_tracker gate (hotfix4) in
            // place even with this hotfix.
            string currentPath = currentMemoryReadout?.currentPath;
            if (arrangement == null && !string.IsNullOrEmpty(currentPath) &&
                currentCDLCDetails.arrangements != null)
            {
                var arrangements = currentCDLCDetails.arrangements;

                // 2a: Prefer non-bonus, non-alternate — first match wins
                // (v0.6.5 hotfix5.1 — restored legacy first-match behavior; the
                // count-and-only-pick-if-one logic from initial hotfix5 was leaving
                // arrangement unresolved when songs had multiple arrangements with type
                // matching currentPath, falling through to the heuristic chain unnecessarily.)
                foreach (var arr in arrangements)
                {
                    if ((arr.type == currentPath || arr.name == currentPath) &&
                        !arr.isBonusArrangement && !arr.isAlternateArrangement)
                    {
                        arrangement = arr;
                        fallbackReason = "current Path \"" + currentPath + "\" + non-bonus filter";
                        break;
                    }
                }

                // 2b: Bonus/alt allowed if no regular match — first match wins
                if (arrangement == null)
                {
                    foreach (var arr in arrangements)
                    {
                        if (arr.type == currentPath || arr.name == currentPath)
                        {
                            arrangement = arr;
                            fallbackReason = "current Path \"" + currentPath + "\" (bonus/alt allowed)";
                            break;
                        }
                    }
                }
            }

            // STEP 3 onwards: Defensive fallback chain (v0.6.5).
            //
            // Reached when both direct arrangementID match AND Path resolution failed.
            // In normal operation this should not happen — Path is read from a stable
            // memory byte that's populated from Rocksmith launch onward. These steps
            // are defense-in-depth for the rare edge case where the Path read failed
            // (e.g. transient memory hiccup during process tear-down).
            //
            // The pre-Path fallback chain (prev-path heuristic backed by
            // SnifferRuntimeState persistence, defaultArrangementType setting) was
            // removed in v0.6.5 cleanup. Path resolution made all of it unreachable.
            if (arrangement == null)
            {
                var arrangements = currentCDLCDetails.arrangements;

                if (arrangements != null && arrangements.Count > 0)
                {
                    // STEP 3: Single-playable-arrangement heuristic
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

                    if (arrangement == null && arrangements.Count == 1)
                    {
                        // STEP 4: only-arrangement-on-song (last resort, even bonus/alternate)
                        arrangement = arrangements[0];
                        fallbackReason = "only-arrangement-on-song heuristic";
                    }
                }
            }

            // DEFERRAL was removed in hotfix5. With Path now available from Rocksmith
            // launch onward (it's a menu-level setting, not waiting for a song to start),
            // there's nothing useful to wait for — Path is either there or we have an
            // edge-case failure that 5 seconds of waiting won't fix. Fall through to
            // unknown logging immediately.

            // Capture Nonstop-mode flag at song START (gameStage may transition by end).
            // Pre-v0.6.8 this flag gated playthrough writes; v0.6.8 lifted the gate
            // once PLAY_arrID made Nonstop arrangement resolution reliable. The flag
            // is still propagated on event args for any downstream consumer that wants
            // the contextual signal, and it's used below to suppress the
            // "Could not resolve arrangement" warning for Nonstop runs — see comment
            // at the warning site.
            //
            // The check uses gameStage strings directly (rather than the v0.6.8
            // mode field) to keep behavior identical to the v0.6.5 hotfix4 definition.
            string startGameStage = currentMemoryReadout?.gameStage;
            currentSongRunWasNonstopMode =
                startGameStage == "nsp_main" ||
                startGameStage == "nonstopplayhub" ||
                startGameStage == "nonstopplaygame";

            // (v0.6.8) Capture MULTIPLAYER-mode flag at song START. Used by
            // PlaythroughHistory and the JS playthrough-tracker to gate writes — full
            // MP support (multi-user note data, per-user arrangements) is a separate
            // larger effort; until that lands, MP plays would produce row-quality
            // issues if persisted.
            //
            // Uses the v0.6.8 gameStage-derived mode field rather than reimplementing
            // gameStage prefix matching for split_game / mp_* / duet_* / h2h_* — the
            // RSMode classifier already covers all of those (see
            // RSMemoryReader.DeriveModeFromGameStage).
            currentSongRunWasMultiplayerMode = currentMemoryReadout?.mode == RSMode.MULTIPLAYER;

            string path;
            string tuning;

            if (arrangement != null)
            {
                path = arrangement.type;
                tuning = arrangement.tuning.TuningName;

                if (fallbackReason != null && !currentSongRunWasNonstopMode)
                {
                    // Suppress the warning when the song was started in Nonstop Play.
                    // v0.6.8's PLAY_arrID chain makes arrangement-ID resolution reliable
                    // in Nonstop, but brief transients during song-to-song transitions
                    // can still produce momentary failures where Path-based fallback
                    // fires; logging those as errors would be noise on every Nonstop
                    // song. The suppression conservatively preserves pre-v0.6.8 quietness
                    // for Nonstop, even though the underlying resolution gap is now
                    // closed in the common case.
                    //
                    // For LaS / SA / other modes, the warning is still useful — it
                    // means either a transient timing race (ID hadn't populated yet at
                    // the read tick) or a genuine ID-mismatch bug worth investigating.
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

            // Note: currentSongRunWasNonstopMode and startGameStage were already
            // computed above (before the warning block) so the warning could
            // suppress itself in Nonstop. No need to recompute here.

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
                tuning = tuning,
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode
            });
        }

        // Floor for case-2 (no-input-boot) detection. Rocksmith's no-input
        // abort only fires before the timer advances meaningfully; once
        // playback is moving, audio-input drop triggers auto-pause instead.
        // 0.1s is well below any legitimate play time and well above the
        // float-near-zero initialization flashes (e.g. 9.2515e-37) that can
        // briefly appear during chart load.
        private const float MIN_PROGRESS_SECONDS = 0.1f;

        /// <summary>
        /// Decides the completed flag for force-end paths (SONG_PLAYING→timer=0,
        /// songID-change force-end, gameStage force-end). Returns true unless one
        /// of the three documented completed=false cases applies:
        ///   1. Pause-driven exit/restart/skip (currentState still SONG_PAUSED
        ///      when force-end fires — UpdateState() hasn't run yet this poll).
        ///   2. No-input boot at song start (timer never advanced past the
        ///      MIN_PROGRESS_SECONDS floor; Rocksmith aborts before playback).
        ///   3. Score Attack 3-strike fail (FailedPhrases >= 3, regardless of
        ///      how close maxTime got to songLength — last-phrase fails are
        ///      still fails).
        /// SA fail is checked FIRST so a last-phrase fail (where maxTime would
        /// otherwise look like a natural completion) still resolves to false.
        /// </summary>
        private bool DetermineCompletedForForceEnd()
        {
            // CASE 3: SA 3-strike fail
            if (currentMemoryReadout?.mode == RSMode.SCOREATTACK &&
                currentMemoryReadout.noteData is ScoreAttackNoteData saData &&
                saData.FailedPhrases >= 3)
            {
                return false;
            }

            // CASE 2: no-input boot at song start
            if (maxTime < MIN_PROGRESS_SECONDS)
            {
                return false;
            }

            // CASE 1: pause-driven exit/restart/skip. State is still SONG_PAUSED
            // when the force-end fires because pause-driven flows trigger the
            // force-end (via chart unload, songID change, or gameStage change)
            // before UpdateState() has a chance to transition the state machine.
            if (currentState == SnifferState.SONG_PAUSED)
            {
                return false;
            }

            // Otherwise: presumed natural completion (e.g. natural-end-of-song
            // that missed the SONG_ENDING transition due to poll cadence race,
            // or an NSP transition where the previous song completed naturally).
            return true;
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
                wasNonstopMode = currentSongRunWasNonstopMode,
                wasMultiplayerMode = currentSongRunWasMultiplayerMode,
                readout = snapshotReadout
            });

            // Reset song-run state so a replay of the SAME song (songID unchanged: restart,
            // exit-and-replay, finish-and-replay) can fire start/end again as a NEW run.
            // We keep lastLogEndedForSongID set so any duplicate end-trigger paths in this
            // same poll cycle (e.g. songID-change AND gameStage-transition both trying to
            // force-end) get blocked; lastLogEndedForSongID is cleared on the next
            // successful LogSongStart.
            lastLogStartedForSongID = null;
            currentSongRunArrangementID = null;
            currentSongRunPath = null;
            currentSongRunTuning = null;
            currentSongRunWasNonstopMode = false;
            currentSongRunWasMultiplayerMode = false;
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
                    // Guard transition with gameStage check (v0.6.7) to prevent
                    // spurious progression into SONG_SELECTED → SONG_STARTING when
                    // RockSniffer attaches to an already-running Rocksmith process,
                    // when Rocksmith restarts while RockSniffer is running, or when
                    // any transient memory garbage briefly reads a non-zero songTimer
                    // in a menu state. Without this guard, the state machine
                    // historically marched forward through menu states based on
                    // junk timer reads and could get stuck in SONG_STARTING.
                    //
                    // Only treat songTimer != 0 as a real song-start signal when
                    // gameStage is one of the values consistent with being in (or
                    // about to be in) a song. Sticky *_pause stages are included
                    // because gameStage doesn't update on resume — see
                    // IsPotentiallyPlayingGameStage for full classification.
                    if (currentMemoryReadout.songTimer != 0 &&
                        IsPotentiallyPlayingGameStage(currentMemoryReadout.gameStage))
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
                    // Escape hatch (v0.6.7): user backed out of song-start before
                    // the timer ever advanced past initTime. Without this branch,
                    // SONG_STARTING was sticky — its only exit was the
                    // songTimer > initTime path above, so if the user pressed
                    // Esc during the loading screen or otherwise aborted before
                    // gameplay began, the state machine would park in
                    // SONG_STARTING indefinitely.
                    //
                    // gameStage observation is the cleanest detector here: when
                    // the user aborts, Rocksmith transitions gameStage back to
                    // a menu/tuner/songreview value. We deliberately do NOT
                    // exit on *_pause gameStages (las_pause, sa_pause, nsp_pause)
                    // because those are sticky and may be lingering from a prior
                    // song's pause that hasn't yet been cleared by a major
                    // Rocksmith stage transition. See IsDefinitelyMenuGameStage
                    // for the full allowlist.
                    //
                    // No song-end event fires here — no song actually started, so
                    // there is nothing to log. The state machine simply unwinds
                    // back to IN_MENUS and the normal startup-detection logic
                    // resumes from there.
                    else if (IsDefinitelyMenuGameStage(currentMemoryReadout.gameStage))
                    {
                        Logger.Log("SONG_STARTING aborted (gameStage={0}, timer never advanced); returning to IN_MENUS", currentMemoryReadout.gameStage);
                        currentState = SnifferState.IN_MENUS;
                    }
                    break;

                case SnifferState.SONG_PLAYING:
                    // Allow small margin at end of song
                    if (currentCDLCDetails != null &&
                        currentMemoryReadout.songTimer >= currentCDLCDetails.songLength - 0.201f)
                    {
                        currentState = SnifferState.SONG_ENDING;
                    }

                    // If the timer goes to 0 without reaching the end, force-end
                    // the song. The completed flag is determined by
                    // DetermineCompletedForForceEnd() — this catches SA 3-strike
                    // fail, no-input boot at start, and natural-completion-missed-
                    // SONG_ENDING-race (the last of which was a v0.6.8 false
                    // negative source — see v0.6.9 release notes).
                    if (currentMemoryReadout.songTimer == 0 &&
                        initTime != float.MaxValue)
                    {
                        completed = DetermineCompletedForForceEnd();

                        LogSongEnd(completed: completed);
                        currentState = SnifferState.IN_MENUS;

                        // Reset pause tracking for next run
                        lowTime = float.MaxValue;
                        initTime = float.MaxValue;
                        maxTime = float.MinValue;
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                        break;
                    }

                    // PAUSE ENTRY (v0.6.7 flag-driven, v0.6.9 transition-gated):
                    // pauseMenuMode at MemoryOffsets.GetPauseMenuModePointer encodes
                    // blocking-overlay state: 0=no overlay, 1=sub-overlay (e.g.
                    // tuner-from-pause), 2=top-level overlay (pause menu, restart
                    // confirmation, etc.).
                    //
                    // v0.6.9: detection now requires the 0 → non-zero TRANSITION of
                    // pauseMenuMode rather than just the current non-zero state. This
                    // prevents a false SONG_PLAYING → SONG_PAUSED firing immediately
                    // after the user clicks Restart from the pause menu: Rocksmith
                    // can briefly keep pauseMenuMode reading 2 for several polls into
                    // the new song's playback before clearing it to 0. Without the
                    // transition gate, the new song's first few polls (once
                    // songTimer > initTime) would fire a spurious "Song Paused!"
                    // followed by "Song Resumed!" once the stale flag cleared.
                    //
                    // The initTime guard is preserved: pauseMenuMode can theoretically
                    // become non-zero during the brief loading-screen window before
                    // the user has actually started playing (e.g. a Tools-menu access
                    // during a transition). Once initTime is captured (timer first
                    // observed > 0), pause detection is enabled.
                    if (previousPauseMenuMode == 0 &&
                        currentMemoryReadout.pauseMenuMode != 0 &&
                        initTime != float.MaxValue &&
                        currentMemoryReadout.songTimer > initTime)
                    {
                        currentState = SnifferState.SONG_PAUSED;
                        Logger.Log("Song Paused! (pauseMenuMode={0} at timer {1:F3})", currentMemoryReadout.pauseMenuMode, currentMemoryReadout.songTimer);
                        paused = true;
                        // Snapshot songTimer for resume-vs-exit disambiguation
                        // (see pauseTimerSnapshot field docs above for rationale).
                        pauseTimerSnapshot = currentMemoryReadout.songTimer;
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
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
                        paused = false;
                    }
                    // PAUSE EXIT (v0.6.7): flag-driven via pauseMenuMode + timer-movement guard.
                    //
                    // Migrated from the v0.6.6 timer-stall heuristic which required
                    // STALL_THRESHOLD polls of timer advancement before recognizing
                    // resume. The flag-driven approach recognizes resume on the
                    // first poll where pauseMenuMode returns to 0 AND the songTimer
                    // has moved from where it was at pause-entry.
                    //
                    // The songTimer != pauseTimerSnapshot guard is critical: when
                    // the user hits Exit from the pause menu, pauseMenuMode flips
                    // to 0 (engine clears the overlay) while songTimer is still
                    // frozen at the pause-point. Without the timer-movement check,
                    // the !isPaused condition alone would fire a spurious
                    // SONG_PAUSED → SONG_PLAYING transition for 1-2 polls until
                    // songTimer drops to 0 and the exit-detection branch above
                    // catches it. The snapshot ensures real resume (timer
                    // re-advances, possibly with a small rewind for "catch up")
                    // is distinguished from exit (timer stays frozen until the
                    // engine fully transitions back to menus).
                    //
                    // Tuner-from-pause is handled correctly without special-
                    // casing: the engine transitions pauseMenuMode from 2 (pause
                    // menu visible) to 1 (tuner sub-overlay) when the user enters
                    // the tuner — still non-zero, so isPaused stays true and this
                    // branch never evaluates. Only when the user fully returns to
                    // gameplay (mode 0) AND the timer demonstrates movement does
                    // SONG_PAUSED exit to SONG_PLAYING.
                    else if (!currentMemoryReadout.isPaused &&
                             currentMemoryReadout.songTimer > initTime &&
                             currentMemoryReadout.songTimer != pauseTimerSnapshot)
                    {
                        currentState = SnifferState.SONG_PLAYING;
                        Logger.Log("Song Resumed! (pauseMenuMode=0 at timer {0:F3}, was paused at {1:F3})", currentMemoryReadout.songTimer, pauseTimerSnapshot);
                        pauseTimerSnapshot = float.MinValue;
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
                        lastObservedTimer = float.MinValue;
                        pauseTimerSnapshot = float.MinValue;
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

        // ─────────────────────────────────────────────────────────────────────
        // gameStage classification helpers (v0.6.7)
        //
        // The state machine's startup transitions (IN_MENUS → SONG_SELECTED, and
        // the SONG_STARTING → IN_MENUS escape hatch) need to distinguish three
        // categories of gameStage values:
        //
        //   1. "Potentially playing"  → las_game, sa_game, nonstopplaygame,
        //                                las_pause, sa_pause, nsp_pause
        //      These either mean the user IS in a song, or MIGHT be in a song
        //      with a sticky *_pause gameStage that hasn't cleared yet (because
        //      Rocksmith only clears *_pause on major stage transitions like
        //      song-end or menu navigation, NOT on resume from pause).
        //
        //   2. "Definitely menu"      → mainmenu, gcpre, learnasong, scoreattack,
        //                                nonstopplay, nsp_main, *_songs,
        //                                *_options, *_tuner, *_songreview,
        //                                panel_*, shop, gc_*, gcade, ge_*,
        //                                mp_*, sm_*
        //      The user is clearly NOT actively playing a song. Includes
        //      *_tuner (whether reached from a pause menu or directly from a
        //      song-select menu — either way, not actively playing) and
        //      *_songreview (the post-song summary).
        //
        //   3. Unknown / unclassified → anything not on either list above
        //      Default to safest behavior: treat as menu for entry guards
        //      (don't progress to SONG_SELECTED), but don't treat as menu for
        //      the SONG_STARTING escape (don't drop out on an unknown value
        //      that might be a real song-start state we haven't catalogued).
        //
        // Stages are compared as exact strings rather than prefix matches to
        // keep classification deterministic. New Rocksmith gameStages we
        // encounter later can be added here as we identify them — the safe
        // default for unknowns is to NOT make state machine decisions based
        // on the gameStage value.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// True if gameStage indicates the user is potentially in a song —
        /// either actively playing or in a sticky *_pause state.
        /// Used to gate IN_MENUS → SONG_SELECTED transitions, preventing
        /// spurious progression based on transient memory garbage during
        /// Rocksmith startup, RockSniffer attach, or any menu navigation
        /// where songTimer briefly reads non-zero.
        /// </summary>
        private static bool IsPotentiallyPlayingGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage)) return false;
            switch (gameStage)
            {
                case "las_game":
                case "sa_game":
                case "nonstopplaygame":
                // *_pause stages included because gameStage doesn't reset on
                // resume — the user could be back in active gameplay with a
                // stale *_pause reading lingering until song-end / menu-nav.
                case "las_pause":
                case "sa_pause":
                case "nsp_pause":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True if gameStage is definitely a menu / tuner / songreview state,
        /// indicating the user is not actively playing a song.
        /// Used by the SONG_STARTING escape hatch to detect when the user
        /// has aborted song-start before the timer ever advanced.
        ///
        /// Deliberately excludes *_pause stages, which are sticky and may be
        /// lingering from a prior song's pause that hasn't been cleared by a
        /// major Rocksmith stage transition yet.
        ///
        /// Unknown stages return false (conservative default — better to stay
        /// in SONG_STARTING and let the timer-based path resolve than to
        /// bail out on an unrecognized value).
        /// </summary>
        private static bool IsDefinitelyMenuGameStage(string gameStage)
        {
            if (string.IsNullOrEmpty(gameStage)) return false;
            switch (gameStage)
            {
                // Top-level menus
                case "mainmenu":
                case "gcpre":
                case "learnasong":
                case "scoreattack":
                case "nonstopplay":
                case "nsp_main":
                // Per-mode menus (song select, options)
                case "las_songs":
                case "las_options":
                // Tuners (whether reached from a song-select menu or from a
                // pause menu — both treated as "not actively playing")
                case "las_tuner":
                case "nsp_tuner":
                // Song-end summary screens
                case "las_songreview":
                case "sa_songreview":
                // Other panels / menus
                case "panel_bib":
                case "shop":
                case "gcade":
                    return true;
                default:
                    // Unknown stages: don't bail out from SONG_STARTING. If a
                    // new "definitely menu" gameStage appears that's not on
                    // this list, it'll be added in a future release once
                    // we've observed and classified it.
                    return false;
            }
        }
    }
}
