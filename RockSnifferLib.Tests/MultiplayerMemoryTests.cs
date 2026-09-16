using System.Text;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Multiplayer;
using RockSnifferLib.SysHelpers;
using RockSnifferLib.Sniffing;
using Xunit;

namespace RockSnifferLib.Tests;

public class MultiplayerMemoryTests
{
    private const uint Module = 0x400000, P1 = 0x3000000, P2 = 0x3100000;
    private const string Arrangement = "11111111111111111111111111111111";
    private sealed class Memory : IReadMemory
    {
        public Dictionary<uint, byte> Data = new();
        private uint allocation = 0x2000000;
        public bool Disposed;
        public int Reads;
        public void Put(uint address, byte[] bytes)
        { for (int i = 0; i < bytes.Length; i++) Data[address + (uint)i] = bytes[i]; }
        public byte[]? Read(uint address, int size)
        {
            Reads++;
            Assert.InRange(size, 1, 128);
            return Enumerable.Range(0, size).All(i => Data.ContainsKey(address + (uint)i))
                ? Enumerable.Range(0, size).Select(i => Data[address + (uint)i]).ToArray() : null;
        }
        public void Chain(uint root, uint leaf, params uint[] offsets)
        {
            uint address = Module + root;
            for (int i = 0; i < offsets.Length; i++)
            {
                var existing = Read(address, 4);
                uint next = i == offsets.Length - 1 ? leaf - offsets[i] :
                    existing != null ? BitConverter.ToUInt32(existing) : allocation += 0x10000;
                Put(address, BitConverter.GetBytes(next));
                address = next + offsets[i];
            }
        }
        public void Dispose() { Disposed = true; }
    }
    private static Memory Ready()
    {
        var m = new Memory();
        m.Chain(0xF6062C, P1, 0xB0, 0x30, 0x18, 4, 0x84, 0);
        m.Chain(0xF60588, P1, 0x134, 0x88, 0x28, 0, 0x24, 0);
        m.Chain(0xF6062C, P2, 0xB0, 0x34, 0x18, 4, 0x74, 0);
        m.Chain(0xF60588, P2, 0x134, 0x88, 0x28, 4, 0x24, 0);
        m.Chain(0xF6062C, 0x3200000, 0xB0, 0x30, 0x538, 8);
        m.Chain(0xF6062C, 0x3300000, 0xB0, 0x34, 0x538, 8);
        m.Put(0x3200000, BitConverter.GetBytes(4f)); m.Put(0x3300000, BitConverter.GetBytes(4f));
        m.Put(Module + 0xF605FC, new byte[] { 2 });
        var stage = new byte[64]; Encoding.ASCII.GetBytes("panel_bib").CopyTo(stage, 0);
        m.Put(Module + 0xF607C9, stage);
        m.Chain(0xF60514, 0x3400000, 0xBC, 0);
        var preview = new byte[128]; Encoding.ASCII.GetBytes("Play_synthetic_Preview").CopyTo(preview, 0);
        m.Put(0x3400000, preview);
        foreach (var address in new[] { P1, P2 })
        {
            var data = new byte[0x48]; BitConverter.GetBytes(111000).CopyTo(data, 8);
            new Guid(Arrangement).ToByteArray().CopyTo(data, 0x20);
            BitConverter.GetBytes(147).CopyTo(data, 0x30);
            BitConverter.GetBytes(49).CopyTo(data, 0x40);
            m.Put(address, data);
        }
        return m;
    }
    [Theory]
    [InlineData(RSEdition.Remastered_Learn_And_Play, 0u)]
    [InlineData(RSEdition.Remastered, 0x1000u)]
    public void ReadsBothEditionLayoutsAndChecksCatalogMembership(RSEdition edition, uint relocation)
    {
        var m = Ready();
        // Relocate only module data; heap objects and pointer-chain steps stay put.
        foreach (var pair in m.Data.Where(p => p.Key >= Module && p.Key < Module + 0x1000000).ToArray())
        {
            m.Data.Remove(pair.Key);
            m.Data[pair.Key - relocation] = pair.Value;
        }
        m.Put(P1 + 0x30, BitConverter.GetBytes(80));
        m.Put(P1 + 0x40, BitConverter.GetBytes(20));
        using var reader = new ExperimentalMultiplayerReader(m, Module, edition);
        var snapshot = reader.Read((selected, ids) => {
            Assert.Equal("synthetic", selected);
            return new SongDetails {
                songID = "synthetic", songLength = 240,
                arrangements = new() { new ArrangementDetails { arrangementID = Arrangement, type = "Lead" } }
            };
        });
        Assert.True(snapshot.Supported);
        Assert.Equal("paused", snapshot.State);
        Assert.Equal("panel_bib", snapshot.Stage);
        Assert.Equal(4, snapshot.ElapsedSeconds);
        Assert.Equal(80, snapshot.Player1!.Accuracy);
        Assert.Equal(75, snapshot.Player2!.Accuracy);
        Assert.Equal("Lead", snapshot.Player1!.Path);
        Assert.Equal("synthetic", snapshot.SongId);
        Assert.Equal(240, snapshot.DurationSeconds);
        var mismatch = reader.Read((_, _) => new SongDetails { songID = "wrong", songLength = 999 });
        Assert.Null(mismatch.SongId); Assert.Null(mismatch.DurationSeconds);
    }
    [Theory]
    [InlineData(RSEdition.Remastered_Just_In_Case_We_Need_It_Beta)]
    [InlineData((RSEdition)999)]
    public void UnmappedEditionDoesNotReadMemory(RSEdition edition)
    {
        var m = Ready();
        int reads = m.Reads;
        using var reader = new ExperimentalMultiplayerReader(m, Module, edition);
        Assert.False(reader.Supported);
        Assert.Equal(new MultiplayerSnapshot(), reader.Read());
        Assert.Equal(reads, m.Reads);
    }
    [Fact]
    public void DifferentEditionDoesNotFallBackToLearnAndPlayAddresses()
    {
        using var reader = new ExperimentalMultiplayerReader(Ready(), Module, RSEdition.Remastered);
        var snapshot = reader.Read();
        Assert.True(snapshot.Supported);
        Assert.Null(snapshot.RunId);
        Assert.Null(snapshot.Player1);
        Assert.Null(snapshot.Player2);
    }
    [Fact] public void FailedMemoryAndBadMarkerNeverBecomeZeroStats()
    {
        var m = Ready(); m.Data.Remove(P1 + 0x40);
        using var reader = new ExperimentalMultiplayerReader(m, Module);
        var s = reader.Read(); Assert.Null(s.RunId); Assert.Null(s.Player1);
        m.Put(P1 + 0x40, new byte[4]); m.Put(P1 + 8, BitConverter.GetBytes(12));
        Assert.Null(reader.Read().Player1);
    }
    [Fact] public void AliasedCounterWalksAreRejectedAndHandleDisposed()
    {
        var m = Ready();
        m.Chain(0xF6062C, P1, 0xB0, 0x34, 0x18, 4, 0x74, 0);
        m.Chain(0xF60588, P1, 0x134, 0x88, 0x28, 4, 0x24, 0);
        var reader = new ExperimentalMultiplayerReader(m, Module);
        Assert.Null(reader.Read().RunId);
        reader.Dispose(); Assert.True(m.Disposed);
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }
}
