using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;

namespace RockSnifferLib.SysHelpers;

internal interface IReadMemory : IDisposable { byte[]? Read(uint address, int size); }

// Kept separate from MemoryHelper: these readers cannot enumerate or write.
internal sealed class ProcessReadMemory : IReadMemory
{
    private readonly SafeProcessHandle handle;

    internal ProcessReadMemory(int pid)
    {
        handle = OpenProcess(0x1010, false, pid);
        if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(); }
    }

    internal bool IsAlive => GetExitCodeProcess(handle, out uint code) && code == 259;
    internal DateTime? StartTimeUtc => GetProcessTimes(handle, out long created, out _, out _, out _)
        ? DateTime.FromFileTimeUtc(created) : null;

    public byte[]? Read(uint address, int size)
    {
        var bytes = new byte[size];
        return ReadProcessMemory(handle, new IntPtr((long)address), bytes, (UIntPtr)size, out var count)
            && count.ToUInt64() == (ulong)size ? bytes : null;
    }

    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address,
        byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation,
        out long exit, out long kernel, out long user);
}
