using System;

namespace RockSnifferLib.RSHelpers;

public static class MemoryOffsets
{
    /// <summary>
    /// Get the pointer to the enumeration flag for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int, int[]) GetEnumerationFlagPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0xF71E10, [0x8, 0x4]),
            RSEdition.Remastered => (0xF71E10 + 0x3080, [0x8, 0x4]),
            RSEdition.Remastered_Learn_And_Play => (0xF71E10 + 0x4080, [0x8, 0x4]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the song ID for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetSongIdPointer(RSEdition edition)
    {
        //Candidate #1: (0x00F5C494, [{ 0xBC, 0x0 ]})
        //Candidate #2: (0x00F80CEC, [{ 0x598, 0x1B8, 0x0 ]})
        //Candidate #3: (0x00F5DAFC, [{ 0x608, 0x1B8, 0x0 ]})
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C494, [0xBC, 0x0]),
            RSEdition.Remastered => (0x00F5C494 + 0x3080, [0xBC, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C494 + 0x4080, [0xBC, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the song timer for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>   
    public static (int entryAddress, int[] offsets) GetSongTimerPointer(RSEdition edition)
    {
        //Weird static address: (0x01567AB0, new int[]{ 0x80, 0x20, 0x10C, 0x244 })
        //Candidate #1: (0x00F5C5AC, [{ 0xB0, 0x538, 0x8 ]})
        //Candidate #2: (0x00F5C4CC, [{ 0x5F0, 0x538, 0x8 ]})
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x538, 0x8]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x538, 0x8]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x538, 0x8]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the arrangement hash for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetArrangementHashPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0x18, 0x18, 0xC, 0x1C0, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the address of the current gameStage string for the given edition.
    /// </summary>
    /// <remarks>
    /// Migrated to a static-address read in v0.6.6 (PoizenJam). The previous
    /// pointer chain (entry 0x00F5C5AC + edition shift, offsets [0x18, 0x18, 0xC, 0x14])
    /// walked into a transient per-screen UI struct that was unreliable in
    /// several common states:
    ///   - SA song-select / song-options / tuner: returned junk strings
    ///     (file path fragments, etc.) because the chain landed in unrelated
    ///     reused buffers.
    ///   - LaS pause and Nonstop pause: never observed at all, because the
    ///     chain didn't track those stages — `las_pause` and `nsp_pause` are
    ///     real Rocksmith gameStages that the chain was silently dropping.
    ///   - Various transient menu transitions: stale or absent values.
    ///
    /// The static address `Rocksmith2014.exe+0xF5F7C9` (Remastered) is the
    /// canonical .data-section buffer Rocksmith's UI code writes for the
    /// current displayed/tracked stage. It tracks correctly across all
    /// observed states including all three modes' pause stages and the SA
    /// song-select / song-options / tuner screens that the chain garbled.
    ///
    /// Discovered (PoizenJam, v0.6.6) using Cheat Engine string-scan for "gcpre"
    /// with the game running, then narrowing the 13 hits by observing which
    /// one round-tripped correctly across mode/menu transitions. The hit at
    /// module+0xF5F7C9 was the canonical writer; bytes at that address are
    /// stored as a literal string buffer (verified via memory viewer:
    /// "main\0..." in main menu, "las_songs\0..." in LaS song-select, etc.).
    ///
    /// Returned as a (entryAddress, []) tuple so the existing FollowPointers
    /// codepath in RSMemoryReader handles it uniformly — empty offsets means
    /// the foreach loop is a no-op and the read happens at base+entry directly.
    ///
    /// KNOWN ENGINE BEHAVIOR (not a reader bug, do not "correct" here):
    /// Rocksmith does not update this cell on pause→resume or pause→restart
    /// transitions for ANY mode. The cell continues reading "*_pause" until
    /// the user navigates to a menu or starts a different song. Consumers
    /// needing actual play/pause state should consult `game_state` (the
    /// SnifferState machine), which handles this correctly via the
    /// timer-stall heuristic in Sniffer.UpdateState().
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived using the +0x3080 (Beta→Remastered)
    /// and +0x4080 (Beta→LaP) shifts that every other pointer in this file uses.
    /// That convention has held for all eight previously-mapped pointers/addresses,
    /// so it is very likely correct here too — but the Beta and Learn_And_Play
    /// values have not been independently verified. If those editions ever read
    /// garbage / empty for gameStage with otherwise-working RockSniffer behavior,
    /// check this address as the first suspect.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets) — offsets is empty
    /// for a direct static read.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetCurrentMenuPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0xF5F7C9 - 0x3080, []),
            RSEdition.Remastered => (0xF5F7C9, []),
            RSEdition.Remastered_Learn_And_Play => (0xF5F7C9 + 0x1000, []),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the note data when in learn a song mode for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetLearnASongNoteDataPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x84, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the note data when in score attack mode for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetScoreAttackNoteDataPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x4C, 0x0]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the current Path (arrangement type) byte for the given edition.
    /// </summary>
    /// <remarks>
    /// Reverse-engineered (PoizenJam, v0.6.5 hotfix5). This is a 1-byte enum at a stable
    /// menu-level address — populated essentially from Rocksmith launch (defaults to 1
    /// for Lead) and only mutated when the user actively switches Path in options or
    /// song-select. Persistent through every gameStage and game state. Invariant to
    /// bonus and alternate arrangements (only encodes the path *type*, not the specific
    /// arrangement).
    ///
    /// Value mapping:
    ///   0x01 → Lead
    ///   0x02 → Rhythm
    ///   0x04 → Bass
    ///   anything else → Unknown
    ///
    /// Crucially, this works in Nonstop Play (where the existing arrangement_hash
    /// pointer fails to populate). It does NOT solve the bonus/alternate ambiguity in
    /// Nonstop — for that, the playthrough_history / playthrough_tracker Nonstop gate
    /// added in hotfix4 stays in place.
    /// </remarks>
    public static (int entryAddress, int[] offsets) GetCurrentPathPointer(RSEdition edition)
    {
        // Discovered (PoizenJam, v0.6.5 hotfix5) using Cheat Engine on Rocksmith Remastered:
        //   CE table entry: Rocksmith2014.exe+00F5F570, offsets [0x1FC, 0x10] (CE display
        //   order — outermost first), read as Byte. Walk order (which FollowPointers
        //   expects) is the reverse: [0x10, 0x1FC].
        //
        // The Beta-build base address below is back-derived from the verified Remastered
        // address using the consistent +0x3080 (Beta→Remastered) and +0x4080 (Beta→LaP)
        // shifts that every other pointer in this file uses. That convention has held for
        // all seven previously-mapped pointers, so it is very likely correct here too —
        // but the Beta and Learn_And_Play values have not been independently verified.
        // If those editions ever read 0x00 for Path (with otherwise-working RockSniffer
        // behavior), check this address as the first suspect.
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C4F0, [0x10, 0x1FC]),
            RSEdition.Remastered => (0x00F5C4F0 + 0x3080, [0x10, 0x1FC]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C4F0 + 0x4080, [0x10, 0x1FC]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the address of the pause-menu mode byte for the given edition.
    /// </summary>
    /// <remarks>
    /// Discovered (PoizenJam, v0.6.7) using Cheat Engine on Rocksmith Remastered.
    /// A 1-byte cell at module+0xF5F5FC encoding which blocking pause-style overlay
    /// is currently active, with the following observed value table:
    ///
    ///     0 — No blocking overlay. Active gameplay, main menus, song select,
    ///         loading screens, song review screens.
    ///     1 — Sub-overlay active. Tuner accessed FROM the pause menu, or
    ///         tuner accessed from the main menu's Tools sub-menu, or other
    ///         sub-prompts reached from a top-level overlay.
    ///     2 — Top-level blocking overlay active. In-song pause menu (Resume/
    ///         Restart/Tuner/Mixer/Exit), Mixer overlay, Restart-confirmation
    ///         prompts, main menu's Tools overlay (the equivalent of an
    ///         in-song pause menu accessed from main menu via SPACE).
    ///
    /// Critically the variable does NOT represent "is the user paused during
    /// gameplay." It represents "is one of the blocking pause-style overlays
    /// active" — which happens to overlap perfectly with mid-song pause when
    /// the user is in a song, but ALSO fires for the Tools menu accessed from
    /// outside any song. Consumers wanting "is paused during a song" should
    /// combine this with a SnifferState (game_state) check.
    ///
    /// Cross-mode validated: tracks correctly in Score Attack, Learn-A-Song,
    /// Nonstop Play, and Guitarcade minigames with no warmup gate (unlike the
    /// earlier GCPauseManager-flag candidate, which required prior Score Attack
    /// gameplay before becoming active).
    ///
    /// Survives game relaunch as a true .data-section static. Confirmed by:
    ///   - Memory neighborhood inspection: surrounding bytes show structured
    ///     .data patterns (ASCII string fragments, aligned small integers,
    ///     installation-ID GUIDs at +0xC0..+0xE0 offsets) consistent with
    ///     compiled-binary static storage rather than heap allocation.
    ///   - Relaunch test: closing Rocksmith, reopening, re-attaching CE
    ///     without scanning, navigating directly to the typed offset — value
    ///     still tracks pause-menu state correctly across all modes.
    ///   - Address neighborhood: sandwiched between two Koko-named .data
    ///     candidates (MustBlockInputsDueToPauseMenu at +F5F545,
    ///     EnablePauseMenu at +F5F5DB) and the previously-validated gameStage
    ///     buffer at +F5F7C9.
    ///
    /// IMPORTANT design note for state-machine consumers: because value=1
    /// (tuner-from-pause) is distinct from value=2 (pause menu) but BOTH
    /// represent "user is in a pause sub-flow," the correct test for
    /// "currently paused" is mode != 0, not mode == 2. This eliminates the
    /// tuner-from-pause edge case that complicated earlier pause-detection
    /// designs — no asymmetric flag-entry / timer-exit gymnastics needed,
    /// since the variable itself never lies about "we are in a pause overlay"
    /// during tuner-from-pause.
    ///
    /// Returned as (entryAddress, []) tuple so the existing FollowPointers
    /// codepath in RSMemoryReader handles it uniformly — empty offsets means
    /// the foreach loop is a no-op and the read happens at base+entry
    /// directly, matching the same pattern used for the gameStage static read.
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived using the +0x3080 (Beta→
    /// Remastered) and +0x4080 (Beta→LaP) shifts that every other pointer
    /// in this file uses. Convention has held for all eleven previously-mapped
    /// pointers/addresses, so very likely correct — but Beta and Learn_And_Play
    /// values have NOT been independently verified. If those editions read
    /// constant zero across all pause states (otherwise-working RockSniffer
    /// behavior), check this address as the first suspect.
    ///
    /// Discovery credit: kokolihapihvi (upstream RockSniffer maintainer)
    /// pointed us at the surrounding memory region by sharing two named
    /// candidate addresses from his RE project (MustBlockInputsDueToPauseMenu,
    /// EnablePauseMenu). Both turned out to be dead in the current Remastered
    /// build (consistently 0 across all states), but the neighborhood inspection
    /// they prompted led directly to this find.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets) — offsets is empty
    /// for a direct static read.</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetPauseMenuModePointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0xF5F5FC - 0x3080, []),
            RSEdition.Remastered => (0xF5F5FC, []),
            RSEdition.Remastered_Learn_And_Play => (0xF5F5FC + 0x1000, []),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }

    /// <summary>
    /// Get the pointer to the currently-loaded arrangement GUID (PLAY_arrID) for the given edition.
    /// </summary>
    /// <remarks>
    /// Reverse-engineered (PoizenJam, v0.6.8) via Cheat Engine on Rocksmith Remastered.
    /// A 16-byte cell holding the currently-loaded arrangement's GUID in Microsoft
    /// little-endian layout (first 3 fields byte-swapped, last 8 bytes sequential).
    /// Read through a 5-deep pointer chain rooted at the same "stable singleton manager
    /// pointers" entry address as the existing arrangement_hash chain — the two chains
    /// differ only in their offset sequences, not their entry point (both Beta-base
    /// 0x00F5C5AC, both Remastered base 0xF5F62C = 0xF5C5AC + 0x3080).
    ///
    /// The 16 raw bytes convert to the standard 32-char uppercase hex form via
    ///     new Guid(bytes).ToString("N").ToUpperInvariant()
    /// matching the format of songDetails.arrangements[].arrangementID for direct
    /// case-sensitive comparison.
    ///
    /// STATE COVERAGE:
    ///   las_game / las_pause          ✓ holds the currently-playing arrangement GUID
    ///   nonstopplaygame / nsp_pause   ✓ same — this is the v0.6.8 fix target.
    ///                                   Nonstop Play was the long-standing gap where
    ///                                   arrangement_hash never populated; PLAY_arrID
    ///                                   finally provides per-arrangement resolution
    ///                                   in Nonstop, distinguishing bonus/alternate
    ///                                   arrangements where Path-byte fallback could
    ///                                   not (see Sniffer.cs arrangement resolution
    ///                                   STEP 1 + STEP 2 for context).
    ///   Nonstop carousel / nsp_tuner  ✗ not populated — the chain may resolve but
    ///                                   the cell isn't valid until song-load proper.
    ///   sa_game / sa_pause            ✗ Score Attack has its own subsystem. The
    ///                                   existing arrangement_hash chain handles SA
    ///                                   correctly and must remain in use for those
    ///                                   gameStages — see RSMemoryReader dispatch.
    ///   Menus, transitions, songreview ✗ not reliable.
    ///
    /// The chain populates the moment a song starts loading (i.e. when the user
    /// advances past the tuner, or when the song is selected if no tuning needed)
    /// and remains valid throughout song play. Across both LaS and Nonstop modes
    /// the same chain handles gameplay and pause, including the pause-after-resume
    /// sticky-state behavior documented for gameStage (see GetCurrentMenuPointer
    /// remarks for that quirk — it does not affect PLAY_arrID, which is per-song).
    ///
    /// EQUIVALENCE WITH arrangement_hash (LaS only):
    /// In LaS gameplay the GUID-converted output of this chain matches the ASCII hex
    /// string output of the arrangement_hash chain byte-for-byte (cross-validated
    /// during discovery by reading both chains simultaneously). They are interchangeable
    /// for LaS resolution. v0.6.8 consolidates on PLAY_arrID for LaS because (a) a
    /// single chain spans both LaS and Nonstop, simplifying dispatch; (b) a 16-byte
    /// binary read is cheaper than a 32-char ASCII string read with null-termination
    /// scan; and (c) it removes the LaS code path's dependency on arrangement_hash,
    /// which has historically been the more fragile of the two chains.
    ///
    /// CHAIN VALIDATION (PoizenJam, v0.6.8):
    ///   - Survived full Rocksmith process restart with correct resolution to new
    ///     heap address (singleton manager re-bind, same pattern observed for the
    ///     existing arrangement_hash, song-timer, and note-data chains).
    ///   - Survived multiple Nonstop entry/exit cycles, including round-trips
    ///     through main menu and back into Nonstop.
    ///   - Tracked correctly across multiple distinct songs and across distinct
    ///     arrangements within the same song (Lead vs Rhythm vs Bass, plus
    ///     bonus / alternate where present).
    ///   - Output matches arrangement_hash in LaS gameplay (cross-validation
    ///     against the previously-trusted chain).
    ///
    /// FORMAT NOTE: the GUID is returned as a string of 32 uppercase hex chars with
    /// no separators, matching the example IDs observed in PSARC-side data (e.g.
    /// EA94EC3F9817673B925B4997C4C0175C). Sniffer.cs cross-references readout.
    /// arrangementID against songDetails.arrangements[].arrangementID using
    /// case-sensitive string equality, so case normalization to upper is required
    /// for downstream matching to succeed.
    ///
    /// EDITION SHIFTS (Beta / LaP): back-derived using the +0x3080 (Beta→Remastered)
    /// and +0x4080 (Beta→LaP) shifts that every other pointer in this file uses.
    /// Convention has held for all twelve previously-mapped pointers/addresses, so
    /// very likely correct here too — but the Beta and Learn_And_Play values have
    /// NOT been independently verified. If those editions ever read all-zero GUIDs
    /// across all gameplay states (with otherwise-working RockSniffer behavior),
    /// check this address as the first suspect.
    /// </remarks>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets).</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static (int entryAddress, int[] offsets) GetPlayArrIDPointer(RSEdition edition)
    {
        // CE table entry: Rocksmith2014.exe+00F5F62C, offsets [0x20, 0x84, 0x4, 0x18, 0xB0]
        // (CE display order — outermost first, i.e. added last to the deepest pointer).
        // Walk order (which FollowPointers expects) is the REVERSE: [0xB0, 0x18, 0x4, 0x84, 0x20].
        // Same convention documented for GetCurrentPathPointer (v0.6.5 hotfix5) and
        // observed for every other multi-offset chain in this file.
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0xB0, 0x18, 0x4, 0x84, 0x20]),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown edition")
        };
    }
}