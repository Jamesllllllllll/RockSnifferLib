using System;
using System.Collections.Generic;
using System.IO;

namespace RockSnifferLib.Library
{
    public sealed record PsarcLibraryRoot(string Id, string Path);

    public enum PsarcLibraryRootStatus
    {
        Ready,
        Partial,
        Unavailable,
        Cancelled,
    }

    public sealed record PsarcLibraryArrangement(
        string ArrangementId,
        string Name,
        string Type,
        bool IsBonus,
        bool IsAlternate,
        string? Tuning
    );

    public sealed record PsarcLibrarySong(
        string SongId,
        string Title,
        string Artist,
        string? Album,
        int Year,
        float LengthSeconds,
        string? ToolkitAuthor,
        string? ToolkitVersion,
        IReadOnlyList<PsarcLibraryArrangement> Arrangements
    );

    public sealed record PsarcLibraryFile(
        string RootId,
        string FilePath,
        long Length,
        DateTime LastWriteTimeUtc,
        string HashAlgorithm,
        string FileHash,
        IReadOnlyList<PsarcLibrarySong> Songs,
        bool Reused
    )
    {
        public bool HasSameFileStamp(FileInfo fileInfo)
        {
            fileInfo.Refresh();
            return fileInfo.Exists &&
                fileInfo.Length == Length &&
                fileInfo.LastWriteTimeUtc == LastWriteTimeUtc;
        }

        public PsarcLibraryFile ReuseForRoot(string rootId)
        {
            return this with { RootId = rootId, Reused = true };
        }
    }

    public sealed record PsarcLibraryError(
        string RootId,
        string? FilePath,
        string Code,
        string Message
    );

    public sealed record PsarcLibraryRootResult(
        PsarcLibraryRoot Root,
        PsarcLibraryRootStatus Status,
        IReadOnlyList<PsarcLibraryFile> Files,
        IReadOnlyList<PsarcLibraryError> Errors
    );

    public sealed record PsarcLibraryScanResult(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        IReadOnlyList<PsarcLibraryRootResult> Roots
    );

    public sealed record PsarcLibraryProgress(
        string RootId,
        int DiscoveredFiles,
        int ProcessedFiles,
        int ReusedFiles,
        int FailedFiles
    );

    public sealed class PsarcLibraryScanOptions
    {
        public int MaxParallelism { get; init; } = 2;

        public IReadOnlyCollection<PsarcLibraryFile> PreviousFiles { get; init; } =
            Array.Empty<PsarcLibraryFile>();

        public IProgress<PsarcLibraryProgress>? Progress { get; init; }
    }
}
