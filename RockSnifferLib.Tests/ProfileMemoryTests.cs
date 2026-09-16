using System.Diagnostics;
using System.Text;
using RockSnifferLib.RSHelpers;
using RockSnifferLib.RSHelpers.Profiles;
using RockSnifferLib.SysHelpers;
using Xunit;

namespace RockSnifferLib.Tests;

public class ProfileMemoryTests
{
    private const uint Module = 0x1000000;
    private const uint Stage = Module + 0xF607C9;
    private const uint Root = Module + 0xF6062C;
    private const uint Leaf = 0x401FC;
    private static readonly ProfileIdentity Profile = new("synthetic-id", "Player One");

    private sealed class Memory : IReadMemory
    {
        internal readonly Dictionary<uint, byte[]> Data = new();
        internal int Reads;
        internal int StageReads;
        internal bool Disposed;
        internal bool ChangeStageDuringWalk;
        private readonly uint stageAddress;
        internal Memory(uint stage = Stage, uint root = Root)
        {
            stageAddress = stage;
            Text(stage, "panel_bib", 64);
            Pointer(root, 0x10000);
            Pointer(0x10018, 0x20000);
            Pointer(0x2003C, 0x30000);
            Pointer(0x30028, 0x40000);
            Text(Leaf, Profile.Name, 128);
        }
        internal void Pointer(uint address, uint value) => Data[address] = BitConverter.GetBytes(value);
        internal void Text(uint address, string text, int size)
        {
            Data[address] = new byte[size];
            Encoding.UTF8.GetBytes(text).CopyTo(Data[address], 0);
        }
        public byte[]? Read(uint address, int size)
        {
            Assert.False(Disposed);
            Assert.InRange(size, 1, 128);
            Assert.InRange(address, 65536u, uint.MaxValue - (uint)size);
            Reads++;
            if (address == stageAddress && ++StageReads == 2 && ChangeStageDuringWalk)
                Text(stageAddress, "main", 64);
            return Data.GetValueOrDefault(address);
        }
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(RSEdition.Remastered_Learn_And_Play, 0xF607C9u, 0xF6062Cu)]
    [InlineData(RSEdition.Remastered, 0xF5F7C9u, 0xF5F62Cu)]
    public void BothEditionsTrackSelectionThenIgnoreDestroyedNameAfterLogin(
        RSEdition edition, uint stage, uint root)
    {
        stage += Module;
        root += Module;
        var memory = new Memory(stage, root);
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile }, edition: edition);
        Assert.True(reader.Supported);
        Assert.Equal(Profile, reader.Read().SelectedProfile);
        memory.Text(stage, "main", 64);
        memory.Data.Remove(root);
        Assert.Equal(Profile, reader.Read().ObservedLoadedProfile);
        Assert.Equal(Profile, reader.Read().ObservedLoadedProfile);
    }

    [Theory]
    [InlineData(Root)]
    [InlineData(0x10018u)]
    [InlineData(0x2003Cu)]
    [InlineData(0x30028u)]
    [InlineData(Leaf)]
    public void FailedOrPartialWalkClearsCandidate(uint address)
    {
        var memory = new Memory();
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        Assert.Equal(Profile, reader.Read().SelectedProfile);
        memory.Data[address] = new byte[3];
        Assert.Null(reader.Read().SelectedProfile);
        memory.Text(Stage, "main", 64);
        Assert.Null(reader.Read().ObservedLoadedProfile);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFu)]
    [InlineData(0xFFFFFFF0u)]
    public void InvalidPointersAreRejectedWithoutWrapping(uint value)
    {
        var memory = new Memory();
        memory.Pointer(Root, value);
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        Assert.Null(reader.Read().SelectedProfileName);
    }

    [Fact]
    public void ModuleAddressOverflowDoesNotReadMemory()
    {
        var memory = new Memory();
        using var reader = new ExperimentalProfileReader(memory, 0xFFFFFF00, new[] { Profile });
        Assert.Null(reader.Read().Stage);
        Assert.Equal(0, memory.Reads);
    }

    [Fact]
    public void StageChangingDuringWalkInvalidatesPendingSelection()
    {
        var memory = new Memory { ChangeStageDuringWalk = true };
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        Assert.Null(reader.Read().SelectedProfile);
        Assert.Null(reader.Read().ObservedLoadedProfile);
    }

    [Theory]
    [InlineData("Player\nOne")]
    [InlineData(" ")]
    [InlineData("")]
    public void ControlOrEmptyNamesAreRejected(string name)
    {
        var memory = new Memory();
        memory.Text(Leaf, name, 128);
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        Assert.Null(reader.Read().SelectedProfileName);
    }

    [Fact]
    public void UnterminatedAndMalformedUtf8NamesAreRejected()
    {
        var memory = new Memory();
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        memory.Data[Leaf] = Enumerable.Repeat((byte)'A', 128).ToArray();
        Assert.Null(reader.Read().SelectedProfileName);
        memory.Data[Leaf] = new byte[128];
        memory.Data[Leaf][0] = 0xC3;
        memory.Data[Leaf][1] = 0x28;
        Assert.Null(reader.Read().SelectedProfileName);
    }

    [Fact]
    public void Utf8NamesUseExactCatalogMatches()
    {
        var profile = new ProfileIdentity("unicode-id", "Joueur \u00c9");
        var memory = new Memory();
        memory.Text(Leaf, profile.Name, 128);
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { profile });
        Assert.Equal(profile, reader.Read().SelectedProfile);
        memory.Text(Stage, "main", 64);
        Assert.Equal(profile, reader.Read().ObservedLoadedProfile);
    }

    [Fact]
    public void StageReadFailureDoesNotRetainLoadedProfile()
    {
        var memory = new Memory();
        using var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile });
        reader.Read();
        memory.Text(Stage, "main", 64);
        Assert.Equal(Profile, reader.Read().ObservedLoadedProfile);
        memory.Data.Remove(Stage);
        Assert.Null(reader.Read().ObservedLoadedProfile);
        memory.Text(Stage, "main", 64);
        Assert.Null(reader.Read().ObservedLoadedProfile);
    }

    [Fact]
    public void ExitedProcessClearsIdentityWithoutFurtherReadsAndDisposeClosesHandle()
    {
        bool alive = true;
        var memory = new Memory();
        var reader = new ExperimentalProfileReader(memory, Module, new[] { Profile }, () => alive);
        reader.Read();
        memory.Text(Stage, "main", 64);
        Assert.Equal(Profile, reader.Read().ObservedLoadedProfile);
        alive = false;
        int reads = memory.Reads;
        Assert.Null(reader.Read().ObservedLoadedProfile);
        Assert.Equal(reads, memory.Reads);
        reader.Dispose();
        reader.Dispose();
        Assert.True(memory.Disposed);
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }

    [Fact]
    public void NativeReadOnlyHandleReportsTheSameProcessLifetime()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        using var memory = new ProcessReadMemory(process.Id);
        Assert.True(memory.IsAlive);
        Assert.Equal(process.StartTime.ToUniversalTime(), memory.StartTimeUtc);
    }

    [Theory]
    [InlineData(RSEdition.Remastered_Just_In_Case_We_Need_It_Beta)]
    [InlineData((RSEdition)999)]
    public void UnmappedEditionNeverProducesIdentity(RSEdition edition)
    {
        using var process = Process.GetCurrentProcess();
        using var reader = new ExperimentalProfileReader(process, edition, new[] { Profile });
        Assert.False(reader.Supported);
        Assert.Equal(new ProfileSnapshot(), reader.Read());
        var memory = new Memory();
        using var synthetic = new ExperimentalProfileReader(memory, Module, new[] { Profile }, edition: edition);
        Assert.Equal(new ProfileSnapshot(), synthetic.Read());
        Assert.Equal(0, memory.Reads);
    }

    [Fact]
    public void DifferentEditionDoesNotFallBackToLearnAndPlayAddresses()
    {
        using var reader = new ExperimentalProfileReader(new Memory(), Module,
            new[] { Profile }, edition: RSEdition.Remastered);
        var snapshot = reader.Read();
        Assert.True(snapshot.Supported);
        Assert.Null(snapshot.SelectedProfile);
        Assert.Null(snapshot.ObservedLoadedProfile);
    }
}
