using RockSnifferLib.RSHelpers;
using System.Security.Cryptography;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class PsarcFileHashTests
{
    [Fact]
    public void Sha256FingerprintUsesTheCompleteFileAndLowercaseHex()
    {
        var directory = Directory.CreateTempSubdirectory("rocksniffer-sha256-");
        var filePath = Path.Combine(directory.FullName, "test_p.psarc");
        var bytes = "complete psarc bytes"u8.ToArray();

        try
        {
            File.WriteAllBytes(filePath, bytes);

            var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var fingerprint = PSARCUtil.GetFileSha256(new FileInfo(filePath));

            Assert.Equal(expected, fingerprint);
            Assert.Matches("^[a-f0-9]{64}$", fingerprint);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LegacyMd5FingerprintRemainsBase64ForExistingCallers()
    {
        var directory = Directory.CreateTempSubdirectory("rocksniffer-md5-");
        var filePath = Path.Combine(directory.FullName, "test_p.psarc");
        var bytes = "legacy psarc bytes"u8.ToArray();

        try
        {
            File.WriteAllBytes(filePath, bytes);

            var expected = Convert.ToBase64String(MD5.HashData(bytes));
            var fingerprint = PSARCUtil.GetFileHash(new FileInfo(filePath));

            Assert.Equal(expected, fingerprint);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
