using RockSnifferLib.RSHelpers.NoteData;
using RockSnifferLib.SysHelpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RockSnifferLib.RSHelpers
{
    public class RSMemoryReader
    {
        private RSMemoryReadout readout = new RSMemoryReadout();
        private RSMemoryReadout prevReadout = new RSMemoryReadout();

        //Process handles
        private readonly Process rsProcess;
        private readonly RSEdition edition;
        private readonly IntPtr rsProcessHandle;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rsProcess"></param>
        /// <param name="edition"></param>
        public RSMemoryReader(Process rsProcess, RSEdition edition)
        {
            this.rsProcess = rsProcess;
            this.edition = edition;

            rsProcessHandle = rsProcess.Handle;
        }

        /// <summary>
        /// Sets the enumerate flag to 1, causing rocksmith to start enumerating
        /// </summary>
        public void TriggerEnumeration()
        {
            IntPtr addr = FollowPointers(MemoryOffsets.GetEnumerationFlagPointer(edition));

            MemoryHelper.WriteBytesToMemory(rsProcessHandle, addr, new byte[] { 0x01 });
        }

        /// <summary>
        /// Read song timer and note data from memory
        /// </summary>
        /// <returns></returns>
        public RSMemoryReadout DoReadout()
        {
            // SONG ID
            //
            // Seems to be a zero terminated string in the format: Play_SONGID_Preview
            string preview_name = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(MemoryOffsets.GetSongIdPointer(edition)));

            //If there was string in memory
            if (preview_name != null)
            {
                //Verify Play_ prefix and _Preview or _Invalid suffix
                //_Invalid suffix is applied to all song previews, and replaces _Preview, when a RSMods user has the "Disable Song Preview" mod enabled.
                //_Invalid is used to prevent the song preview from being played in-game, but in this case we want to know when that event is triggered.
                if (preview_name.StartsWith("Play_") && (preview_name.EndsWith("_Preview") || preview_name.EndsWith("_Invalid")))
                {
                    //Remove Play_ prefix and _Preview or _Invalid suffix
                    string song_id = preview_name.Substring(5, preview_name.Length - 13);

                    // RESET arrangementID ON SONG-ID CHANGE (v0.6.5):
                    // The internal `readout` field persists across DoReadout calls. Without
                    // this reset, a valid hash from the PREVIOUS song lingers in
                    // readout.arrangementID even after the user has navigated to a different
                    // song — leading to stale-arrangement reports during menu browsing. We
                    // null it here so the next memory read for the new song starts fresh.
                    if (readout.songID != song_id)
                    {
                        readout.arrangementID = null;
                    }

                    //Assign to readout
                    readout.songID = song_id;
                }
            }

            // SONG TIMER
            ReadSongTimer(FollowPointers(MemoryOffsets.GetSongTimerPointer(edition)));

            // GAME STAGE
            //
            // (Moved above ARRANGEMENT ID in v0.6.8 — the arrangement-id read now
            // dispatches by gameStage, so gameStage must be resolved first. Pre-v0.6.8
            // these two blocks were in the reverse order; no semantic change beyond
            // the dispatch requirement.)
            //
            // Static address read (v0.6.6) — see MemoryOffsets.GetCurrentMenuPointer
            // for the discovery story and full migration notes. Briefly: replaces
            // a pointer chain that returned garbage in several menu states (SA
            // song-select, song-options, tuner) and silently dropped LaS / Nonstop
            // pause stages entirely. The static buffer at module+0xF5F7C9
            // (Remastered) is Rocksmith's canonical gameStage cell.
            //
            // Length >= 4 guard: kept from the prior implementation as a defense
            // against transient sub-4-char writes during stage transitions. With
            // the static read this is rarely if ever exercised, but harmless.
            //
            // KNOWN: gameStage will NOT update on pause→resume or pause→restart
            // for any mode. This is Rocksmith engine behavior, not a reader bug.
            // Consumers needing actual play/pause state should use `game_state`
            // (SnifferState), which derives play/pause via timer-stall detection
            // in Sniffer.UpdateState().
            string game_stage = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(MemoryOffsets.GetCurrentMenuPointer(edition)));

            //If we got a game stage
            if (game_stage != null)
            {
                //Verify that it is at least 4 characters long, to filter out more garbage
                if (game_stage.Length >= 4)
                {
                    readout.gameStage = game_stage;
                }
            }

            // ARRANGEMENT ID
            //
            // Dispatch by gameStage (v0.6.8). Two memory chains expose arrangement-id
            // data in different states:
            //
            //   PLAY_arrID chain (v0.6.8 — MemoryOffsets.GetPlayArrIDPointer):
            //     Reads a 16-byte raw GUID in Microsoft LE layout, converts to the
            //     standard 32-char uppercase hex form via
            //         new Guid(bytes).ToString("N").ToUpperInvariant()
            //     for comparison against songDetails.arrangements[].arrangementID.
            //     Used for:
            //       las_game / las_pause        — Learn-A-Song gameplay and pause
            //       nonstopplaygame / nsp_pause — Nonstop Play gameplay and pause
            //     For LaS the chain is interchangeable with arrangement_hash (cross-
            //     validated identical output during discovery) and v0.6.8 consolidates
            //     on it. For Nonstop this is the v0.6.8 fix target — it finally
            //     provides per-arrangement resolution in Nonstop, where the legacy
            //     arrangement_hash chain never populated.
            //
            //   arrangement_hash chain (legacy — MemoryOffsets.GetArrangementHashPointer):
            //     Reads a 32-char ASCII hex string directly from memory. Used for:
            //       sa_game / sa_pause — Score Attack has its own subsystem; PLAY_arrID
            //                            does NOT track it. The legacy chain handles
            //                            SA correctly and must remain in use.
            //       All other gameStages — preserves pre-v0.6.8 behavior in menu /
            //                              song-select / song-review / transition
            //                              states. May return junk or stale values
            //                              in those states; filtered the same way as
            //                              in v0.6.7 (see VALIDATION below). Worth
            //                              revisiting in a future cleanup pass once
            //                              PLAY_arrID has soaked in the field, but
            //                              explicitly out of scope for v0.6.8.
            //
            // FORMAT VALIDATION (v0.6.5, retained):
            // IsValidArrangementHash rejects null/empty/wrong-length strings and any
            // non-hex character. Catches structural garbage from either chain — for
            // arrangement_hash this is the longstanding case of un-initialized memory
            // returning song titles or album-art URN fragments; for PLAY_arrID it
            // catches all-zero or unresolved-chain reads (e.g. between songs in
            // Nonstop carousel where the chain may resolve but the cell isn't
            // populated yet).
            //
            // CANDIDATE VALIDATION (v0.6.5, at Sniffer.cs lines 394-410, unchanged):
            // Cross-references readout.arrangementID against currentCDLCDetails.
            // arrangements[] and nulls it on no-match. Catches format-valid but
            // song-mismatched IDs (stale values from previously-played or browsed
            // songs persisting in the read cell). Chain-agnostic — applies equally
            // to both PLAY_arrID and arrangement_hash output, because both produce
            // 32-char hex strings consumed identically downstream.
            //
            // PERSISTENCE: readout.arrangementID is persistent across DoReadout calls
            // until either (a) the songID changes (resetting it to null at the top of
            // DoReadout) or (b) a fresh read here passes IsValidArrangementHash and
            // overwrites it. On a bad read either chain produces null/invalid; the
            // field retains its prior good value and the next poll re-attempts. Same
            // "fail then retry" pattern as v0.6.7.
            bool usePlayArrIDChain = readout.gameStage == "las_game"
                                  || readout.gameStage == "las_pause"
                                  || readout.gameStage == "nonstopplaygame"
                                  || readout.gameStage == "nsp_pause";

            string resolved_arrangement_id;
            if (usePlayArrIDChain)
            {
                resolved_arrangement_id = ReadPlayArrIDFromMemory(
                    FollowPointers(MemoryOffsets.GetPlayArrIDPointer(edition)));
            }
            else
            {
                resolved_arrangement_id = MemoryHelper.ReadStringFromMemory(
                    rsProcessHandle,
                    FollowPointers(MemoryOffsets.GetArrangementHashPointer(edition)));
            }

            if (IsValidArrangementHash(resolved_arrangement_id))
            {
                readout.arrangementID = resolved_arrangement_id;
            }

            // CURRENT PATH (v0.6.5 hotfix5)
            //
            // The user's currently-selected Path (arrangement type) at the menu level.
            // 1-byte enum at a stable address. Populated essentially from Rocksmith launch
            // (defaults to 0x01 / Lead) and only updates when the user actively switches
            // Path in options or song-select. Persistent across all gameStages and game
            // states. Crucially works in Nonstop Play, where arrangement_hash fails.
            //
            // Value mapping: 0x01=Lead, 0x02=Rhythm, 0x04=Bass. Anything else => Unknown
            // (treated as empty string so the resolution chain in Sniffer.cs falls through
            // to the heuristic-based fallbacks).
            //
            // Wrapped in try/catch because IF the pointer chain ever returns IntPtr.Zero
            // (unlikely given how stable this address is, but possible during process
            // tear-down or mid-launch races), ReadByteFromMemory would throw on the
            // resulting null read. Keeping path-resolution failures non-fatal keeps the
            // rest of the readout flowing.
            try
            {
                IntPtr pathAddr = FollowPointers(MemoryOffsets.GetCurrentPathPointer(edition));
                if (pathAddr != IntPtr.Zero)
                {
                    byte pathByte = MemoryHelper.ReadByteFromMemory(rsProcessHandle, pathAddr);
                    readout.currentPathByte = pathByte;
                    readout.currentPath = pathByte switch
                    {
                        0x01 => "Lead",
                        0x02 => "Rhythm",
                        0x04 => "Bass",
                        _ => ""
                    };
                }
            }
            catch
            {
                // Best-effort read — leave currentPathByte/currentPath at their default values.
                // This shouldn't happen in practice (pointer chain has been observed stable),
                // but defensive coding keeps a transient memory hiccup from killing the poll.
            }

            // PAUSE MENU MODE (v0.6.7)
            //
            // Direct read of Rocksmith's pause-menu mode byte — a static .data
            // cell at module+0xF5F5FC (Remastered) that encodes blocking-overlay
            // depth: 0=no overlay, 1=sub-overlay (e.g. tuner-from-pause),
            // 2=top-level overlay (pause menu, Mixer, Tools menu). See
            // MemoryOffsets.GetPauseMenuModePointer for the full state table
            // and discovery context.
            //
            // Cross-mode validated (SA, LaS, NSP, Guitarcade) and verified to
            // survive game relaunch as a true static. Used by Sniffer.UpdateState
            // for first-poll-instant SONG_PLAYING ↔ SONG_PAUSED transitions,
            // replacing the prior timer-stall heuristic.
            //
            // Defensive try/catch around the read, same pattern as currentPath
            // above — keeps a transient memory hiccup from killing the poll.
            // On failure, pauseMenuMode stays at its prior value (or 0 on
            // first poll) and the next successful poll resyncs. isPaused
            // is always derived from pauseMenuMode in lock-step.
            try
            {
                IntPtr pauseModeAddr = FollowPointers(MemoryOffsets.GetPauseMenuModePointer(edition));
                if (pauseModeAddr != IntPtr.Zero)
                {
                    byte modeByte = MemoryHelper.ReadByteFromMemory(rsProcessHandle, pauseModeAddr);
                    readout.pauseMenuMode = modeByte;
                    readout.isPaused = modeByte != 0;
                }
            }
            catch
            {
                // Best-effort read — leave pauseMenuMode/isPaused at their default / prior values.
            }

            // NOTE DATA
            //
            // For learn a song:
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] {0xB0, 0x18, 0x4, 0x84, 0x0})
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] {0x5F0, 0x18, 0x4, 0x84, 0x0})
            //
            // For score attack:
            //Candidate #1: FollowPointers(0x00F5C5AC, new int[] { 0xB0, 0x18, 0x4, 0x4C, 0x0 })
            //Candidate #2: FollowPointers(0x00F5C4CC, new int[] { 0x5F0, 0x18, 0x4, 0x4C, 0x0 })

            //If note data is not valid, try the next mode
            //Learn a song
            if (!ReadNoteData(FollowPointers(MemoryOffsets.GetLearnASongNoteDataPointer(edition))))
            {
                //Score attack
                if (!ReadScoreAttackNoteData(FollowPointers(MemoryOffsets.GetScoreAttackNoteDataPointer(edition))))
                {
                    readout.mode = RSMode.UNKNOWN;
                }
            }

            //Copy over everything when a song is running
            if (readout.songTimer > 0)
            {
                readout.CopyTo(ref prevReadout);
            }

            //Always copy over important fields
            prevReadout.songID = readout.songID;
            prevReadout.gameStage = readout.gameStage;
            prevReadout.songTimer = readout.songTimer;

            // currentPath is a menu-level setting that's stable across all game states —
            // always propagate, same as songID/gameStage. Without this, prevReadout would
            // only get the path during active gameplay (songTimer > 0), and consumers
            // querying `prevReadout.currentPath` while in song-select would see stale data.
            prevReadout.currentPathByte = readout.currentPathByte;
            prevReadout.currentPath = readout.currentPath;

            // pauseMenuMode reflects engine overlay state and can flip on user input
            // (pause button) at any songTimer value, including songTimer == 0 during
            // loading. Propagate every poll regardless of songTimer, same rationale
            // as currentPath above — otherwise consumers would see stale pause state
            // during the brief window when pause is first registered.
            prevReadout.pauseMenuMode = readout.pauseMenuMode;
            prevReadout.isPaused = readout.isPaused;

            // Always propagate arrangementID (v0.6.5):
            // The previous behavior of only updating arrangementID when songTimer > 0
            // caused two problems:
            //   (1) When the user picked an arrangement in the LaS song-options screen
            //       (songTimer is 0), the new arrangement_hash never reached prevReadout.
            //       LogSongStartIfPossible then fired with stale data.
            //   (2) When the Sniffer.cs cross-reference cleared prevReadout.arrangementID
            //       (because of a stale value), nothing re-populated it from `readout`
            //       on the next poll until songTimer > 0 — perpetuating the null state.
            // Always propagating means the cross-reference clearing is per-poll only;
            // the next memory read can resupply a valid value immediately.
            prevReadout.arrangementID = readout.arrangementID;

            return prevReadout;
        }

        /// <summary>
        /// Validates that a string is a 32-character hexadecimal hash matching the format of
        /// Rocksmith arrangement IDs (MD5 hashes serialized as hex). Returns false for null,
        /// empty, wrong length, or any non-hex character.
        ///
        /// Used to filter out junk reads from the arrangement_hash memory pointer when the
        /// game hasn't yet populated that location with a valid hash (e.g. during song-load
        /// transitions, especially in Nonstop Play).
        /// </summary>
        private static bool IsValidArrangementHash(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 32)
            {
                return false;
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'A' && c <= 'F') ||
                      (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private IntPtr FollowPointers((int entryAddress, int[] offsets) tuple)
        {
            return FollowPointers(tuple.entryAddress, tuple.offsets);
        }

        private IntPtr FollowPointers(int entryAddress, int[] offsets)
        {
            //If the process has exited, don't try to read memory
            if (rsProcess.HasExited)
            {
                return IntPtr.Zero;
            }

            //Get base address
            IntPtr baseAddress = rsProcess.MainModule.BaseAddress;

            //Add entry address
            IntPtr finalAddress = IntPtr.Add(baseAddress, entryAddress);

            //Add offsets
            foreach (int offset in offsets)
            {
                finalAddress = MemoryHelper.FollowPointer(rsProcessHandle, finalAddress, offset);

                //If any of the offsets points to 0, return zero
                if (finalAddress.ToInt32() == offset)
                {
                    return IntPtr.Zero;
                }
            }

            //Return the final address
            return finalAddress;
        }

        private void ReadSongTimer(IntPtr timerAddress)
        {
            //Read float from memory and assign field on readout
            readout.songTimer = MemoryHelper.ReadFloatFromMemory(rsProcessHandle, timerAddress);
        }

        /// <summary>
        /// Reads 16 raw bytes from the PLAY_arrID chain (v0.6.8) and converts them
        /// to the canonical 32-char uppercase hex string format that matches the
        /// layout of songDetails.arrangements[].arrangementID.
        ///
        /// The bytes are interpreted as a Microsoft GUID — first 3 fields in
        /// little-endian byte order, last 8 bytes sequential — via the .NET
        /// Guid(byte[]) constructor. ToString("N") returns the GUID as 32 hex
        /// chars with no separators; ToUpperInvariant normalizes case for the
        /// case-sensitive cross-reference at Sniffer.cs lines 394-410.
        ///
        /// Returns null on:
        ///   - IntPtr.Zero from FollowPointers (chain broken — should not happen
        ///     in the four dispatched gameStages where the chain has been validated
        ///     stable, but defensively handled to keep failures non-fatal).
        ///   - Any exception during the byte read or GUID construction. Guarded
        ///     defensively for parity with the v0.6.7 currentPath and pauseMenuMode
        ///     reads — under normal operation ReadBytesFromMemory always returns a
        ///     16-byte buffer and new Guid(byte[16]) does not throw, so this catch
        ///     is for transient memory hiccups during process tear-down or attach
        ///     races, not expected steady-state behavior.
        ///
        /// A null return causes DoReadout to leave readout.arrangementID at its
        /// prior value (existing v0.6.5 "fail then retry on next poll" semantics
        /// from the conditional IsValidArrangementHash assignment).
        /// </summary>
        private string ReadPlayArrIDFromMemory(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                byte[] bytes = MemoryHelper.ReadBytesFromMemory(rsProcessHandle, address, 16);
                if (bytes == null || bytes.Length != 16)
                {
                    return null;
                }
                return new Guid(bytes).ToString("N").ToUpperInvariant();
            }
            catch
            {
                // Best-effort read — null return leaves readout.arrangementID at its
                // prior value, next poll retries. Same defensive pattern as currentPath
                // and pauseMenuMode reads above.
                return null;
            }
        }

        private bool ReadNoteData(IntPtr structAddress)
        {
            //Check validity
            //No null pointers
            if (structAddress == IntPtr.Zero)
            {
                return false;
            }

            //This seems to be a magic number that is at this value when the pointer is valid
            if (MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0008)) != 111000)
            {
                return false;
            }

            //Assign mode
            readout.mode = RSMode.LEARNASONG;

            //Read note data
            readout.noteData = MemoryHelper.ReadStructureFromMemory<LearnASongNoteData>(rsProcessHandle, structAddress);

            return true;
        }

        private bool ReadScoreAttackNoteData(IntPtr structAddress)
        {
            //Check validity
            //No null pointers
            if (structAddress == IntPtr.Zero)
            {
                return false;
            }

            //This seems to be a magic number that is at this value when the pointer is valid
            if (MemoryHelper.ReadInt32FromMemory(rsProcessHandle, IntPtr.Add(structAddress, 0x0008)) != 111000)
            {
                return false;
            }

            readout.mode = RSMode.SCOREATTACK;

            //Read note data
            readout.noteData = MemoryHelper.ReadStructureFromMemory<ScoreAttackNoteData>(rsProcessHandle, structAddress);

            return true;
        }
    }
}
