using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockSnifferLib.Library
{
    public sealed class PsarcLibraryIndexer
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private readonly IPsarcFileInspector inspector;

        public PsarcLibraryIndexer(IPsarcFileInspector? inspector = null)
        {
            this.inspector = inspector ?? new DefaultPsarcFileInspector();
        }

        public async Task<PsarcLibraryScanResult> ScanAsync(
            IEnumerable<PsarcLibraryRoot> roots,
            PsarcLibraryScanOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(roots);
            options ??= new PsarcLibraryScanOptions();
            if (options.MaxParallelism <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options.MaxParallelism),
                    "Maximum parallelism must be greater than zero."
                );
            }

            var startedAt = DateTimeOffset.UtcNow;
            var normalizedRoots = NormalizeRoots(roots);
            var previousFiles = options.PreviousFiles
                .GroupBy(file => NormalizePath(file.FilePath), PathComparer)
                .ToDictionary(group => group.Key, group => group.First(), PathComparer);
            var claimedFiles = new ConcurrentDictionary<string, byte>(PathComparer);
            var results = new List<PsarcLibraryRootResult>(normalizedRoots.Count);

            foreach (var root in normalizedRoots)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    results.Add(CancelledRoot(root));
                    continue;
                }

                results.Add(await ScanRootAsync(
                    root,
                    options.MaxParallelism,
                    previousFiles,
                    claimedFiles,
                    options.Progress,
                    cancellationToken
                ).ConfigureAwait(false));
            }

            return new PsarcLibraryScanResult(
                startedAt,
                DateTimeOffset.UtcNow,
                results
            );
        }

        private async Task<PsarcLibraryRootResult> ScanRootAsync(
            PsarcLibraryRoot root,
            int maxParallelism,
            IReadOnlyDictionary<string, PsarcLibraryFile> previousFiles,
            ConcurrentDictionary<string, byte> claimedFiles,
            IProgress<PsarcLibraryProgress>? progress,
            CancellationToken cancellationToken
        )
        {
            if (!Directory.Exists(root.Path))
            {
                return new PsarcLibraryRootResult(
                    root,
                    PsarcLibraryRootStatus.Unavailable,
                    Array.Empty<PsarcLibraryFile>(),
                    new[]
                    {
                        new PsarcLibraryError(
                            root.Id,
                            null,
                            "root_missing",
                            "The library folder is not available."
                        ),
                    }
                );
            }

            var errors = new ConcurrentBag<PsarcLibraryError>();
            var files = EnumeratePsarcFiles(root, errors, cancellationToken)
                .Where(path => claimedFiles.TryAdd(path, 0))
                .ToArray();
            var processedCount = 0;
            var reusedCount = 0;
            var failedCount = 0;
            ReportProgress();
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledRoot(root, errors);
            }

            var indexedFiles = new ConcurrentBag<PsarcLibraryFile>();
            using var parallelism = new SemaphoreSlim(maxParallelism, maxParallelism);
            var tasks = files.Select(async path =>
            {
                await parallelism.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileInfo = new FileInfo(path);
                    if (
                        previousFiles.TryGetValue(path, out var previous) &&
                        previous.HasSameFileStamp(fileInfo)
                    )
                    {
                        indexedFiles.Add(previous.ReuseForRoot(root.Id));
                        Interlocked.Increment(ref reusedCount);
                        return;
                    }

                    var inspected = await inspector
                        .InspectAsync(root.Id, fileInfo, cancellationToken)
                        .ConfigureAwait(false);
                    indexedFiles.Add(inspected);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The root result below records cancellation once for the scan.
                }
                catch (PsarcFileInspectionException error)
                {
                    Interlocked.Increment(ref failedCount);
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        path,
                        error.Code,
                        error.Message
                    ));
                }
                catch (UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref failedCount);
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        path,
                        "file_access_denied",
                        "The PSARC file could not be accessed."
                    ));
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failedCount);
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        path,
                        "file_read_failed",
                        "The PSARC file could not be inspected."
                    ));
                }
                finally
                {
                    Interlocked.Increment(ref processedCount);
                    ReportProgress();
                    parallelism.Release();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CancelledRoot(root, errors, indexedFiles);
            }

            var orderedFiles = indexedFiles
                .OrderBy(file => file.FilePath, PathComparer)
                .ToArray();
            var orderedErrors = errors
                .OrderBy(error => error.FilePath, PathComparer)
                .ThenBy(error => error.Code, StringComparer.Ordinal)
                .ToArray();
            var status = cancellationToken.IsCancellationRequested
                ? PsarcLibraryRootStatus.Cancelled
                : orderedErrors.Length > 0
                    ? PsarcLibraryRootStatus.Partial
                    : PsarcLibraryRootStatus.Ready;

            return new PsarcLibraryRootResult(root, status, orderedFiles, orderedErrors);

            void ReportProgress()
            {
                progress?.Report(new PsarcLibraryProgress(
                    root.Id,
                    files.Length,
                    Volatile.Read(ref processedCount),
                    Volatile.Read(ref reusedCount),
                    Volatile.Read(ref failedCount)
                ));
            }
        }

        private static IReadOnlyList<string> EnumeratePsarcFiles(
            PsarcLibraryRoot root,
            ConcurrentBag<PsarcLibraryError> errors,
            CancellationToken cancellationToken
        )
        {
            var discovered = new HashSet<string>(PathComparer);
            var pending = new Stack<string>();
            pending.Push(root.Path);

            while (pending.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var directory = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*_p.psarc"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        discovered.Add(NormalizePath(file));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        directory,
                        "directory_access_denied",
                        "A library folder could not be accessed."
                    ));
                    continue;
                }
                catch (Exception)
                {
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        directory,
                        "directory_read_failed",
                        "A library folder could not be read."
                    ));
                    continue;
                }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                            pending.Push(child);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            errors.Add(new PsarcLibraryError(
                                root.Id,
                                child,
                                "directory_access_denied",
                                "A library folder could not be accessed."
                            ));
                        }
                        catch (IOException)
                        {
                            errors.Add(new PsarcLibraryError(
                                root.Id,
                                child,
                                "directory_read_failed",
                                "A library folder could not be read."
                            ));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        directory,
                        "directory_access_denied",
                        "A library folder could not be accessed."
                    ));
                }
                catch (Exception)
                {
                    errors.Add(new PsarcLibraryError(
                        root.Id,
                        directory,
                        "directory_read_failed",
                        "A library folder could not be read."
                    ));
                }
            }

            return discovered.OrderBy(path => path, PathComparer).ToArray();
        }

        private static List<PsarcLibraryRoot> NormalizeRoots(IEnumerable<PsarcLibraryRoot> roots)
        {
            var normalized = new List<PsarcLibraryRoot>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(PathComparer);

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root.Id))
                {
                    throw new ArgumentException("Every library root must have an id.", nameof(roots));
                }
                if (string.IsNullOrWhiteSpace(root.Path))
                {
                    throw new ArgumentException("Every library root must have a path.", nameof(roots));
                }
                if (!ids.Add(root.Id))
                {
                    throw new ArgumentException($"Duplicate library root id: {root.Id}", nameof(roots));
                }

                var path = NormalizePath(root.Path);
                if (!paths.Add(path))
                {
                    throw new ArgumentException($"Duplicate library root path: {path}", nameof(roots));
                }
                normalized.Add(root with { Path = path });
            }

            return normalized;
        }

        private static string NormalizePath(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static PsarcLibraryRootResult CancelledRoot(
            PsarcLibraryRoot root,
            IEnumerable<PsarcLibraryError>? errors = null,
            IEnumerable<PsarcLibraryFile>? files = null
        )
        {
            var combinedErrors = (errors ?? Array.Empty<PsarcLibraryError>()).ToList();
            combinedErrors.Add(new PsarcLibraryError(
                root.Id,
                null,
                "scan_cancelled",
                "The library scan was cancelled."
            ));
            return new PsarcLibraryRootResult(
                root,
                PsarcLibraryRootStatus.Cancelled,
                (files ?? Array.Empty<PsarcLibraryFile>()).ToArray(),
                combinedErrors
            );
        }
    }
}
