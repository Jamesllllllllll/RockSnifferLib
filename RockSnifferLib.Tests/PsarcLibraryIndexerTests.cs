using RockSnifferLib.Library;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class PsarcLibraryIndexerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"rocksniffer-library-tests-{Guid.NewGuid():N}"
    );

    public PsarcLibraryIndexerTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public async Task MissingRootIsUnavailableWithoutInspection()
    {
        var inspector = new FakeInspector();
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(new[]
        {
            new PsarcLibraryRoot("backup", Path.Combine(temporaryDirectory, "missing")),
        });

        var root = Assert.Single(result.Roots);
        Assert.Equal(PsarcLibraryRootStatus.Unavailable, root.Status);
        Assert.Equal("root_missing", Assert.Single(root.Errors).Code);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ScansOnlyWindowsPsarcFilesRecursively()
    {
        var nested = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "nested"));
        CreateFile("first_p.psarc");
        CreateFile(Path.Combine("nested", "second_p.psarc"));
        CreateFile("ignored_m.psarc");
        CreateFile("ignored.psarc");
        var inspector = new FakeInspector();
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(new[]
        {
            new PsarcLibraryRoot("backup", temporaryDirectory),
        });

        var root = Assert.Single(result.Roots);
        Assert.Equal(PsarcLibraryRootStatus.Ready, root.Status);
        Assert.Equal(2, root.Files.Count);
        Assert.Equal(2, inspector.CallCount);
        Assert.Contains(root.Files, file => file.FilePath == Path.Combine(nested.FullName, "second_p.psarc"));
    }

    [Fact]
    public async Task ReusesUnchangedPreviousFileWithoutInspection()
    {
        var file = CreateFile("cached_p.psarc");
        var previous = CreateLibraryFile("old-root", file, hash: "known-hash");
        var inspector = new FakeInspector();
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(
            new[] { new PsarcLibraryRoot("backup", temporaryDirectory) },
            new PsarcLibraryScanOptions { PreviousFiles = new[] { previous } }
        );

        var reused = Assert.Single(Assert.Single(result.Roots).Files);
        Assert.True(reused.Reused);
        Assert.Equal("backup", reused.RootId);
        Assert.Equal("known-hash", reused.FileHash);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ReinspectsFileWhenItsStampChanges()
    {
        var file = CreateFile("changed_p.psarc");
        var previous = CreateLibraryFile("backup", file, hash: "old-hash");
        File.AppendAllText(file.FullName, "changed");
        file.Refresh();
        var inspector = new FakeInspector();
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(
            new[] { new PsarcLibraryRoot("backup", temporaryDirectory) },
            new PsarcLibraryScanOptions { PreviousFiles = new[] { previous } }
        );

        var inspected = Assert.Single(Assert.Single(result.Roots).Files);
        Assert.False(inspected.Reused);
        Assert.Equal("fake-hash", inspected.FileHash);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task FileFailureDoesNotStopTheRestOfTheRoot()
    {
        CreateFile("good_p.psarc");
        CreateFile("bad_p.psarc");
        var inspector = new FakeInspector(path =>
            Path.GetFileName(path).StartsWith("bad", StringComparison.OrdinalIgnoreCase)
                ? new PsarcFileInspectionException("file_parse_failed", "Unreadable chart")
                : null
        );
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(new[]
        {
            new PsarcLibraryRoot("backup", temporaryDirectory),
        });

        var root = Assert.Single(result.Roots);
        Assert.Equal(PsarcLibraryRootStatus.Partial, root.Status);
        Assert.Single(root.Files);
        Assert.Equal("file_parse_failed", Assert.Single(root.Errors).Code);
        Assert.Equal(2, inspector.CallCount);
    }

    [Fact]
    public async Task OverlappingRootsInspectTheSamePathOnlyOnce()
    {
        var nested = Directory.CreateDirectory(Path.Combine(temporaryDirectory, "nested"));
        CreateFile(Path.Combine("nested", "only_p.psarc"));
        var inspector = new FakeInspector();
        var indexer = new PsarcLibraryIndexer(inspector);

        var result = await indexer.ScanAsync(new[]
        {
            new PsarcLibraryRoot("parent", temporaryDirectory),
            new PsarcLibraryRoot("nested", nested.FullName),
        });

        Assert.Equal(2, result.Roots.Count);
        Assert.Single(result.Roots[0].Files);
        Assert.Empty(result.Roots[1].Files);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task ReportsDiscoveredProcessedReusedAndFailedCounts()
    {
        var reusedFile = CreateFile("reused_p.psarc");
        CreateFile("good_p.psarc");
        CreateFile("bad_p.psarc");
        var previous = CreateLibraryFile("backup", reusedFile, hash: "known-hash");
        var progress = new RecordingProgress();
        var inspector = new FakeInspector(path =>
            Path.GetFileName(path).StartsWith("bad", StringComparison.OrdinalIgnoreCase)
                ? new PsarcFileInspectionException("file_parse_failed", "Unreadable chart")
                : null
        );
        var indexer = new PsarcLibraryIndexer(inspector);

        await indexer.ScanAsync(
            new[] { new PsarcLibraryRoot("backup", temporaryDirectory) },
            new PsarcLibraryScanOptions
            {
                PreviousFiles = new[] { previous },
                Progress = progress,
            }
        );

        var final = Assert.Single(progress.Values.Where(value => value.ProcessedFiles == 3));
        Assert.Equal(3, final.DiscoveredFiles);
        Assert.Equal(1, final.ReusedFiles);
        Assert.Equal(1, final.FailedFiles);
    }

    public void Dispose()
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private FileInfo CreateFile(string relativePath)
    {
        var path = Path.Combine(temporaryDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return new FileInfo(path);
    }

    private static PsarcLibraryFile CreateLibraryFile(
        string rootId,
        FileInfo file,
        string hash
    )
    {
        file.Refresh();
        return new PsarcLibraryFile(
            rootId,
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc,
            "md5",
            hash,
            Array.Empty<PsarcLibrarySong>(),
            false
        );
    }

    private sealed class FakeInspector : IPsarcFileInspector
    {
        private readonly Func<string, Exception?> failureFactory;
        private int callCount;

        public int CallCount => callCount;

        public FakeInspector(Func<string, Exception?>? failureFactory = null)
        {
            this.failureFactory = failureFactory ?? (_ => null);
        }

        public Task<PsarcLibraryFile> InspectAsync(
            string rootId,
            FileInfo fileInfo,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref callCount);
            var failure = failureFactory(fileInfo.FullName);
            if (failure != null)
            {
                return Task.FromException<PsarcLibraryFile>(failure);
            }

            fileInfo.Refresh();
            return Task.FromResult(new PsarcLibraryFile(
                rootId,
                fileInfo.FullName,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                "md5",
                "fake-hash",
                Array.Empty<PsarcLibrarySong>(),
                false
            ));
        }
    }

    private sealed class RecordingProgress : IProgress<PsarcLibraryProgress>
    {
        public List<PsarcLibraryProgress> Values { get; } = [];

        public void Report(PsarcLibraryProgress value)
        {
            lock (Values)
            {
                Values.Add(value);
            }
        }
    }
}
