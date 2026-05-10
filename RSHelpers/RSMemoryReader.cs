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

            // ARRANGEMENT HASH
            //
            // This is set to the arrangement persistent id while playing a song.
            //
            // VALIDATION (added in v0.6.5):
            // The memory pointer for arrangement_hash leaks junk values when not initialized
            // (e.g. song titles like "Fear Inoculum", album-art URN strings like
            // "urn:image:dds:album_...", etc.) — leftover bytes from whatever previously occupied
            // that memory region. Real arrangement IDs are 32-character lowercase or uppercase
            // hex MD5 hashes. We reject anything that doesn't match that shape so the JS layer
            // doesn't have to guess whether it's looking at a hash or garbage.
            //
            // Stale values (a valid 32-hex hash from the *previous* song persisting into the
            // current song's polls) are NOT caught here — they pass format validation. Those
            // are handled at the Sniffer.cs layer where we have access to the current song's
            // arrangement list and can cross-reference.
            string arrangement_hash = MemoryHelper.ReadStringFromMemory(rsProcessHandle, FollowPointers(MemoryOffsets.GetArrangementHashPointer(edition)));
            if (IsValidArrangementHash(arrangement_hash))
            {
                readout.arrangementID = arrangement_hash;
            }

            // GAME STAGE
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
