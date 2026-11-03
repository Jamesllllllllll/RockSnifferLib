using System.Text;
using RockSnifferLib.RSHelpers;
using Rocksmith2014PsarcLib.Psarc;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Xunit;

namespace RockSnifferLib.Tests;

public sealed class ArrangementEntryRecoveryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"count\":null}")]
    public void UnreadableEntryDoesNotPreventReadingTheFollowingEntry(string invalidContents)
    {
        var path = CreateArchive(("bad.json", invalidContents), ("good.json", "{\"count\":42}"));
        try
        {
            using var archive = new PsarcFile(path);
            var invalid = archive.TOC.Entries.Single(entry => entry.Path == "bad.json");
            var valid = archive.TOC.Entries.Single(entry => entry.Path == "good.json");

            Assert.False(PSARCUtil.TryReadArrangementEntry<JsonObjectAsset<Manifest>>(archive, invalid, out var failed));
            Assert.Null(failed);
            Assert.True(PSARCUtil.TryReadArrangementEntry<JsonObjectAsset<Manifest>>(archive, valid, out var recovered));
            Assert.Equal(42, recovered!.JsonObject.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TruncatedSngDoesNotPreventReadingAnotherEntry()
    {
        var path = CreateArchive(("broken.sng", "J"), ("good.txt", "readable"));
        try
        {
            using var archive = new PsarcFile(path);
            Assert.False(PSARCUtil.TryReadArrangementEntry<SngAsset>(archive, archive.TOC.Entries[1], out _));
            Assert.True(PSARCUtil.TryReadArrangementEntry<TextPsarcAsset>(archive, archive.TOC.Entries[2], out var recovered));
            Assert.Equal("readable", recovered!.Text);
            Assert.False(PSARCUtil.TryReadArrangementEntry<SngAsset>(archive, null, out _));
        }
        finally { File.Delete(path); }
    }

    public sealed class Manifest { public int Count { get; set; } }

    // Synthetic unencrypted PSARC. No game or copyrighted chart data is included.
    private static string CreateArchive(params (string Path, string Text)[] entries)
    {
        var data = new[] { Encoding.UTF8.GetBytes(string.Join("\n", entries.Select(entry => entry.Path))) }
            .Concat(entries.Select(entry => Encoding.UTF8.GetBytes(entry.Text))).ToArray();
        var blockCount = data.Count(bytes => bytes.Length > 0);
        var tocLength = 32 + 30 * data.Length + 2 * blockCount;
        var path = Path.Combine(Path.GetTempPath(), $"arrangement-recovery-{Guid.NewGuid():N}.psarc");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        void BigEndian(ulong value, int width)
        {
            for (var i = width - 1; i >= 0; i--) writer.Write((byte)(value >> (8 * i)));
        }
        writer.Write("PSAR"u8); BigEndian(0x10004, 4); writer.Write("zlib"u8);
        BigEndian((ulong)tocLength, 4); BigEndian(30, 4); BigEndian((ulong)data.Length, 4);
        BigEndian(65536, 4); BigEndian(0, 4);
        var offset = tocLength;
        var block = 0;
        foreach (var bytes in data)
        {
            writer.Write(new byte[16]); BigEndian((ulong)block, 4);
            BigEndian((ulong)bytes.Length, 5); BigEndian((ulong)offset, 5);
            offset += bytes.Length;
            if (bytes.Length > 0) block++;
        }
        foreach (var bytes in data.Where(bytes => bytes.Length > 0)) BigEndian((ulong)bytes.Length, 2);
        foreach (var bytes in data) writer.Write(bytes);
        return path;
    }
}
