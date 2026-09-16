using RockSnifferLib.SysHelpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RockSnifferLib.RSHelpers.Profiles;

/// <summary>
/// Read-only profile selection/login observer using the host's edition. Create before
/// login and poll every 100-250ms. Dispose and recreate for every game process or
/// profile-catalog change. A reader attached after login reports unknown.
/// </summary>
public sealed class ExperimentalProfileReader : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object sync = new();
    private readonly IReadMemory? memory;
    private readonly uint module;
    private readonly ExperimentalMemoryOffsets offsets;
    private readonly ProfileTracker tracker;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Func<bool> isAlive;
    private bool disposed;
    /// <summary>An edition layout is available; this does not certify live-game validation.</summary>
    public bool Supported { get; }

    /// <param name="process">One running Rocksmith process. Access errors may throw.</param>
    /// <param name="edition">Edition identified by the host, as used by its single-player reader.</param>
    /// <param name="knownProfiles">Snapshot of saved profile IDs/names from the host.
    /// Duplicate names/IDs cannot establish identity. An empty catalog permits
    /// selection text only, never an inferred loaded profile.</param>
    public ExperimentalProfileReader(Process process, RSEdition edition,
        IEnumerable<ProfileIdentity> knownProfiles)
    {
        ArgumentNullException.ThrowIfNull(process);
        tracker = new ProfileTracker(knownProfiles);
        isAlive = () => false;
        if (!ExperimentalMemoryOffsets.TryCreate(edition, out offsets)) return;
        if (!OperatingSystem.IsWindows()) return;
        var started = process.StartTime.ToUniversalTime();
        var image = process.MainModule;
        if (image == null) return;
        module = checked((uint)image.BaseAddress.ToInt64());
        var handle = new ProcessReadMemory(process.Id);
        // Bind to a process lifetime, not just a potentially reused PID.
        if (handle.StartTimeUtc != started || !handle.IsAlive)
        {
            handle.Dispose();
            return;
        }
        memory = handle;
        isAlive = () => handle.IsAlive;
        Supported = true;
    }

    internal ExperimentalProfileReader(IReadMemory memory, uint module,
        IEnumerable<ProfileIdentity> knownProfiles, Func<bool>? isAlive = null,
        RSEdition edition = RSEdition.Remastered_Learn_And_Play)
    {
        tracker = new ProfileTracker(knownProfiles);
        this.memory = memory;
        this.module = module;
        this.isAlive = isAlive ?? (() => true);
        Supported = ExperimentalMemoryOffsets.TryCreate(edition, out offsets);
    }

    public ProfileSnapshot Read()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Supported) return new ProfileSnapshot();
            if (!isAlive()) return tracker.Update(clock.Elapsed.TotalSeconds, null, null);
            string? stage = Text(Add(module, offsets.Stage), 64, ascii: true);
            string? name = null;
            if (ProfileTracker.IsSelection(stage))
            {
                // RSMods CurrentSelectedUser: dereference THEN add each offset.
                name = Text(Walk(offsets.GameRoot, 0x18, 0x3C, 0x28, 0x1FC), 128, ascii: false);
                // A menu transition during the walk must not validate stale text.
                if (stage != Text(Add(module, offsets.Stage), 64, ascii: true)) stage = null;
            }
            return tracker.Update(clock.Elapsed.TotalSeconds, stage, name);
        }
    }

    private static uint Add(uint address, uint offset) => (ulong)address + offset > uint.MaxValue
        ? 0 : address + offset;
    private byte[]? Bytes(uint address, int size)
    {
        if (address < 65536 || size < 1 || size > 128 || (ulong)address + (uint)size > uint.MaxValue)
            return null;
        var bytes = memory!.Read(address, size);
        return bytes?.Length == size ? bytes : null;
    }
    private uint Walk(uint root, params uint[] offsets)
    {
        uint address = Add(module, root);
        foreach (uint offset in offsets)
        {
            var bytes = Bytes(address, 4);
            if (bytes == null) return 0;
            uint next = BitConverter.ToUInt32(bytes, 0);
            if (next < 65536) return 0;
            address = Add(next, offset);
        }
        return address;
    }
    private string? Text(uint address, int size, bool ascii)
    {
        var bytes = Bytes(address, size);
        if (bytes == null) return null;
        int end = Array.IndexOf(bytes, (byte)0);
        if (end <= 0) return null;
        if (ascii)
            for (int i = 0; i < end; i++) if (bytes[i] < 32 || bytes[i] > 126) return null;
        try
        {
            var value = StrictUtf8.GetString(bytes, 0, end);
            foreach (char c in value) if (char.IsControl(c)) return null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (DecoderFallbackException) { return null; }
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; memory?.Dispose(); }
    }
}
