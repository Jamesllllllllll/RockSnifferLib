namespace RockSnifferLib.RSHelpers;

// These readers share the single-player edition root. Relative locations were
// observed on Learn & Play; Remastered is available for experimental testing.
internal readonly record struct ExperimentalMemoryOffsets(uint GameRoot)
{
    public uint SongId => GameRoot - 0x118;
    public uint AlternatePlayerRoot => GameRoot - 0xA4;
    public uint Pause => GameRoot - 0x30;
    public uint Stage => GameRoot + 0x19D;

    public static bool TryCreate(RSEdition edition, out ExperimentalMemoryOffsets offsets)
    {
        offsets = default;
        if (edition is not (RSEdition.Remastered or RSEdition.Remastered_Learn_And_Play))
            return false;
        offsets = new((uint)MemoryOffsets.GetSongTimerPointer(edition).entryAddress);
        return true;
    }
}
