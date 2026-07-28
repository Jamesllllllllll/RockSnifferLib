using RockSnifferLib.RSHelpers;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class PSARCUtilTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"rocksniffer-psarc-tests-{Guid.NewGuid():N}"
    );

    public PSARCUtilTests()
    {
        Directory.CreateDirectory(temporaryDirectory);
    }

    [Fact]
    public void StablePSARCRejectsZeroBytePlaceholder()
    {
        var file = CreateFile("download_p.psarc", []);

        var ready = PSARCUtil.TryWaitForStablePSARC(
            file,
            out _,
            maxAttempts: 2,
            delayMilliseconds: 0,
            requiredStableObservations: 2
        );

        Assert.False(ready);
    }

    [Fact]
    public void StablePSARCRejectsInvalidHeader()
    {
        var contents = new byte[32];
        "NOPE"u8.CopyTo(contents);
        var file = CreateFile("invalid_p.psarc", contents);

        var ready = PSARCUtil.TryWaitForStablePSARC(
            file,
            out _,
            maxAttempts: 2,
            delayMilliseconds: 0,
            requiredStableObservations: 2
        );

        Assert.False(ready);
    }

    [Fact]
    public void StablePSARCAcceptsUnchangedHeaderAndCapturesSnapshot()
    {
        var file = CreatePSARC("complete_p.psarc");

        var ready = PSARCUtil.TryWaitForStablePSARC(
            file,
            out var snapshot,
            maxAttempts: 2,
            delayMilliseconds: 0,
            requiredStableObservations: 2
        );

        Assert.True(ready);
        Assert.Equal(file.Length, snapshot.Length);
        Assert.True(PSARCUtil.MatchesPSARCFileSnapshot(file, snapshot));
    }

    [Fact]
    public void SnapshotDetectsFileGrowth()
    {
        var file = CreatePSARC("growing_p.psarc");
        Assert.True(
            PSARCUtil.TryWaitForStablePSARC(
                file,
                out var snapshot,
                maxAttempts: 1,
                delayMilliseconds: 0,
                requiredStableObservations: 1
            )
        );

        using (var stream = new FileStream(file.FullName, FileMode.Append, FileAccess.Write))
        {
            stream.WriteByte(0);
        }

        Assert.False(PSARCUtil.MatchesPSARCFileSnapshot(file, snapshot));
    }

    [Fact]
    public void SharedReadDoesNotPreventDeletingDownloadPlaceholder()
    {
        var file = CreatePSARC("replaceable_p.psarc");

        using var stream = PSARCUtil.OpenReadShared(file);
        File.Delete(file.FullName);

        Assert.False(File.Exists(file.FullName));
    }

    public void Dispose()
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private FileInfo CreatePSARC(string name)
    {
        var contents = new byte[32];
        "PSAR"u8.CopyTo(contents);
        return CreateFile(name, contents);
    }

    private FileInfo CreateFile(string name, ReadOnlySpan<byte> contents)
    {
        var path = Path.Combine(temporaryDirectory, name);
        File.WriteAllBytes(path, contents.ToArray());
        return new FileInfo(path);
    }
}
