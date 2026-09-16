using RockSnifferLib.RSHelpers.NoteData;
using System;

namespace RockSnifferLib.RSHelpers.Multiplayer;

// Keep the legacy single-player pointers and mutable readout away from a
// validated multiplayer session. The existing HTTP serializer can publish
// the additional snapshot without changing numeric modes or note-data fields.
internal static class MultiplayerReadout
{
    internal static bool IsActive(MultiplayerSnapshot? snapshot) =>
        snapshot is { Supported: true } && snapshot.State != "inactive";

    internal static RSMemoryReadout Read(MultiplayerSnapshot? snapshot, Func<RSMemoryReadout> legacyRead)
    {
        var readout = IsActive(snapshot)
            ? new RSMemoryReadout
            {
                songID = snapshot!.SongId ?? "",
                gameStage = snapshot.Stage ?? "",
                mode = RSMode.MULTIPLAYER,
                noteData = default(LearnASongNoteData),
            }
            : legacyRead();
        readout.experimentalMultiplayer = snapshot;
        return readout;
    }
}
