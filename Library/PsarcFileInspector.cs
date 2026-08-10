using RockSnifferLib.RSHelpers;
using RockSnifferLib.Sniffing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockSnifferLib.Library
{
    public interface IPsarcFileInspector
    {
        Task<PsarcLibraryFile> InspectAsync(
            string rootId,
            FileInfo fileInfo,
            CancellationToken cancellationToken
        );
    }

    public sealed class PsarcFileInspectionException : Exception
    {
        public string Code { get; }

        public PsarcFileInspectionException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public PsarcFileInspectionException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }
    }

    public sealed class DefaultPsarcFileInspector : IPsarcFileInspector
    {
        public Task<PsarcLibraryFile> InspectAsync(
            string rootId,
            FileInfo fileInfo,
            CancellationToken cancellationToken
        )
        {
            return Task.Run(() => Inspect(rootId, fileInfo, cancellationToken), cancellationToken);
        }

        private static PsarcLibraryFile Inspect(
            string rootId,
            FileInfo fileInfo,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PSARCUtil.TryGetReadyPSARCFileSnapshot(
                fileInfo,
                out var readySnapshot,
                cancellationToken: cancellationToken
            ))
            {
                throw new PsarcFileInspectionException(
                    "file_unstable",
                    "The PSARC file is incomplete, unavailable, or still changing."
                );
            }

            string fileHash;
            try
            {
                fileHash = PSARCUtil.GetFileSha256(fileInfo, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new PsarcFileInspectionException(
                    "file_hash_failed",
                    "The PSARC file could not be fingerprinted.",
                    error
                );
            }

            if (!PSARCUtil.MatchesPSARCFileSnapshot(fileInfo, readySnapshot))
            {
                throw new PsarcFileInspectionException(
                    "file_changed",
                    "The PSARC file changed while it was being fingerprinted."
                );
            }

            Dictionary<string, SongDetails>? songDetails;
            try
            {
                songDetails = PSARCUtil.ReadPSARCHeaderData(fileInfo, fileHash);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new PsarcFileInspectionException(
                    "file_parse_failed",
                    "The PSARC file metadata could not be read.",
                    error
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (songDetails == null)
            {
                throw new PsarcFileInspectionException(
                    "file_parse_failed",
                    "The PSARC file did not contain readable song metadata."
                );
            }

            if (!PSARCUtil.MatchesPSARCFileSnapshot(fileInfo, readySnapshot))
            {
                throw new PsarcFileInspectionException(
                    "file_changed",
                    "The PSARC file changed while its metadata was being read."
                );
            }

            var songs = songDetails.Values
                .OrderBy(song => song.songID, StringComparer.OrdinalIgnoreCase)
                .Select(ToLibrarySong)
                .ToArray();

            return new PsarcLibraryFile(
                rootId,
                fileInfo.FullName,
                readySnapshot.Length,
                readySnapshot.LastWriteTimeUtc,
                "sha256",
                fileHash,
                songs,
                false
            );
        }

        private static PsarcLibrarySong ToLibrarySong(SongDetails song)
        {
            var arrangements = song.arrangements
                .Select(arrangement => new PsarcLibraryArrangement(
                    arrangement.arrangementID ?? string.Empty,
                    arrangement.name ?? string.Empty,
                    arrangement.type ?? string.Empty,
                    arrangement.isBonusArrangement,
                    arrangement.isAlternateArrangement,
                    arrangement.tuning?.TuningName
                ))
                .ToArray();

            return new PsarcLibrarySong(
                song.songID ?? string.Empty,
                song.songName ?? string.Empty,
                song.artistName ?? string.Empty,
                song.albumName,
                song.albumYear,
                song.songLength,
                song.toolkit?.author,
                song.toolkit?.version,
                arrangements
            );
        }
    }
}
