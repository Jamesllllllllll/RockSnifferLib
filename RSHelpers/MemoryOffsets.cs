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
    /// Get the pointer to the current menu for the given edition
    /// </summary>
    /// <param name="edition"></param>
    /// <returns>A tuple of (entry address, pointer offsets)</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>   
    public static (int entryAddress, int[] offsets) GetCurrentMenuPointer(RSEdition edition)
    {
        return edition switch
        {
            RSEdition.Remastered_Just_In_Case_We_Need_It_Beta => (0x00F5C5AC, [0x18, 0x18, 0xC, 0x14]),
            RSEdition.Remastered => (0x00F5C5AC + 0x3080, [0x18, 0x18, 0xC, 0x14]),
            RSEdition.Remastered_Learn_And_Play => (0x00F5C5AC + 0x4080, [0x18, 0x18, 0xC, 0x14]),
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
}