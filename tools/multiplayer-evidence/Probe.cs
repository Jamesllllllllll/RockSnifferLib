// Standalone Windows PowerShell 5.1 collector; see THIRD-PARTY-NOTICES.txt for upstream attribution.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace RockListEvidence
{
    public interface IMemory { byte[] Read(uint address, int size); }
    public sealed class Hop
    {
        public string At, Pointer, Offset;
        public bool Read;
    }
    public sealed class Walk
    {
        public string Id, Root, Address, Status;
        public List<Hop> Hops = new List<Hop>();
        internal uint Value;
    }
    public sealed class Note
    {
        public string Address, Arrangement, Status;
        public int? Marker, Hits, Misses, Streak, Best, MissStreak;
        public double? Accuracy;
        internal string RawGuid;
    }
    public sealed class Frame
    {
        public List<Walk> Walks = new List<Walk>();
        public Note P1Primary, P1Secondary, P2Primary, P2Secondary;
        public List<Note> Candidates = new List<Note>();
        public double? Timer1, Timer2, SinglePlayerTimer;
        public int? Pause;
        public string Stage, StageStatus, PreviewStatus, Song;
        public string P1Status, P2Status;
        public bool DistinctPlayers, TimersAgree;
    }
    public sealed class Aliases
    {
        private Dictionary<string, string> names = new Dictionary<string, string>();
        public string Get(string kind, string raw)
        {
            if (String.IsNullOrEmpty(raw)) return null;
            string key = kind + ":" + raw;
            if (!names.ContainsKey(key)) names.Add(key, kind + "-" + (names.Count + 1));
            return names[key];
        }
    }
    public sealed class Probe
    {
        private readonly IMemory memory;
        private readonly uint module, root;
        private readonly Aliases aliases;
        public Probe(IMemory memory, uint module, uint root, Aliases aliases)
        { this.memory = memory; this.module = module; this.root = root; this.aliases = aliases; }
        public static string Hex(uint value) { return "0x" + value.ToString("X8"); }
        private byte[] Read(uint address, int size)
        {
            if (address < 65536 || size < 1 || size > 128 || (ulong)address + (uint)size > UInt32.MaxValue) return null;
            return memory.Read(address, size);
        }
        public Walk Follow(string id, uint entry, params uint[] offsets)
        {
            Walk w = new Walk { Id = id, Root = Hex(entry), Status = "ok" };
            ulong start = (ulong)module + entry;
            if (start > UInt32.MaxValue) { w.Status = "overflow"; return w; }
            uint a = (uint)start;
            foreach (uint offset in offsets)
            {
                byte[] b = Read(a, 4);
                Hop hop = new Hop { At = Hex(a), Offset = Hex(offset), Read = b != null };
                w.Hops.Add(hop);
                if (b == null) { w.Status = "unreadable_pointer"; return w; }
                uint p = BitConverter.ToUInt32(b, 0); hop.Pointer = Hex(p);
                if (p < 65536 || (ulong)p + offset > UInt32.MaxValue) { w.Status = "invalid_pointer"; return w; }
                a = p + offset;
            }
            w.Value = a; w.Address = Hex(a); return w;
        }
        // Only interpreted numeric fields and an alias are exported, never raw blocks/GUIDs.
        public static Note Decode(uint address, byte[] b, Aliases aliases)
        {
            Note n = new Note { Address = Hex(address), Status = "unreadable" };
            if (b == null || b.Length < 0x48) return n;
            n.Marker = BitConverter.ToInt32(b, 8);
            if (n.Marker != 111000) { n.Status = "wrong_marker"; return n; }
            byte[] guidBytes = new byte[16]; Array.Copy(b, 0x20, guidBytes, 0, 16);
            Guid guid = new Guid(guidBytes);
            n.Hits = BitConverter.ToInt32(b, 0x30); n.Misses = BitConverter.ToInt32(b, 0x40);
            n.Streak = BitConverter.ToInt32(b, 0x34); n.Best = BitConverter.ToInt32(b, 0x3C);
            n.MissStreak = BitConverter.ToInt32(b, 0x44);
            n.Status = guid == Guid.Empty ? "empty_guid" :
                n.Hits < 0 || n.Misses < 0 || n.Streak < 0 || n.Best < n.Streak || n.Best > n.Hits ||
                n.MissStreak < 0 || n.MissStreak > n.Misses ? "invalid_counters" : "ok";
            if (guid != Guid.Empty)
            {
                n.RawGuid = guid.ToString("N");
                n.Arrangement = aliases.Get("arrangement", n.RawGuid);
            }
            long total = (long)n.Hits.Value + n.Misses.Value;
            if (n.Status == "ok" && total > 0) n.Accuracy = 100.0 * n.Hits.Value / total;
            return n;
        }
        public Note NoteAt(uint address) { return Decode(address, Read(address, 0x48), aliases); }
        private static string Compare(Note a, Note b)
        {
            if (a.Status != "ok" || b.Status != "ok") return "invalid_read";
            if (a.Address != b.Address) return "paths_disagree";
            if (a.RawGuid != b.RawGuid || a.Hits != b.Hits || a.Misses != b.Misses || a.Streak != b.Streak ||
                a.Best != b.Best || a.MissStreak != b.MissStreak) return "changed_between_reads";
            return "ok";
        }
        private double? Float(uint a)
        {
            byte[] b = Read(a, 4); if (b == null) return null;
            float f = BitConverter.ToSingle(b, 0); return Single.IsNaN(f) || Single.IsInfinity(f) ? (double?)null : f;
        }
        private string Text(uint a, int size)
        {
            byte[] b = Read(a, size); if (b == null) return null;
            int end = Array.IndexOf(b, (byte)0); if (end < 0) return null;
            for (int i = 0; i < end; i++) if (b[i] < 32 || b[i] > 126) return null;
            return Encoding.ASCII.GetString(b, 0, end);
        }
        public Frame Capture(uint[] candidates)
        {
            Frame f = new Frame();
            f.Walks.Add(Follow("p1-primary", root, 0xB0, 0x30, 0x18, 4, 0x84, 0));
            f.Walks.Add(Follow("p1-secondary", root - 0xA4, 0x134, 0x88, 0x28, 0, 0x24, 0));
            f.Walks.Add(Follow("p2-primary", root, 0xB0, 0x34, 0x18, 4, 0x74, 0));
            f.Walks.Add(Follow("p2-secondary", root - 0xA4, 0x134, 0x88, 0x28, 4, 0x24, 0));
            f.Walks.Add(Follow("timer-1", root, 0xB0, 0x30, 0x538, 8));
            f.Walks.Add(Follow("timer-2", root, 0xB0, 0x34, 0x538, 8));
            f.Walks.Add(Follow("single-timer", root, 0xB0, 0x538, 8));
            f.Walks.Add(Follow("preview", root - 0x118, 0xBC, 0));
            f.Walks.Add(Follow("pause", root - 0x30));
            f.Walks.Add(Follow("stage", root + 0x19D));
            f.P1Primary = NoteAt(f.Walks[0].Value); f.P1Secondary = NoteAt(f.Walks[1].Value);
            f.P2Primary = NoteAt(f.Walks[2].Value); f.P2Secondary = NoteAt(f.Walks[3].Value);
            f.P1Status = Compare(f.P1Primary, f.P1Secondary); f.P2Status = Compare(f.P2Primary, f.P2Secondary);
            f.DistinctPlayers = f.P1Status == "ok" && f.P2Status == "ok" && f.P1Primary.Address != f.P2Primary.Address;
            f.Timer1 = Float(f.Walks[4].Value); f.Timer2 = Float(f.Walks[5].Value);
            f.SinglePlayerTimer = Float(f.Walks[6].Value);
            f.TimersAgree = f.Timer1.HasValue && f.Timer2.HasValue && f.Timer1 >= 0 && f.Timer2 >= 0 &&
                f.Timer1 <= 86400 && f.Timer2 <= 86400 && Math.Abs(f.Timer1.Value - f.Timer2.Value) <= .05;
            byte[] pause = Read(f.Walks[8].Value, 1); f.Pause = pause == null ? (int?)null : pause[0];
            string stage = Text(f.Walks[9].Value, 64);
            string[] allowed = { "main", "profile", "profiles", "profile_select", "las_songs", "las_options", "las_game",
                "las_pause", "las_tuner", "las_tuner_ingame", "split_game", "panel_bib", "tuner", "mp_main", "mp_options",
                "gcpre", "sa_game", "sa_pause", "nonstopplaygame", "nsp_pause" };
            f.StageStatus = stage == null ? "unreadable_or_nontext" : Array.IndexOf(allowed, stage) >= 0 ? "known" : "unrecognized_text";
            f.Stage = f.StageStatus == "known" ? stage : null;
            string preview = Text(f.Walks[7].Value, 128);
            bool validPreview = preview != null && preview.StartsWith("Play_", StringComparison.Ordinal) &&
                (preview.EndsWith("_Preview", StringComparison.Ordinal) || preview.EndsWith("_Invalid", StringComparison.Ordinal));
            f.PreviewStatus = validPreview ? "recognized" : preview == null ? "unreadable_or_nontext" : "unrecognized_text";
            if (validPreview) f.Song = aliases.Get("song", preview.Substring(5, preview.Length - 13));
            if (candidates != null) foreach (uint candidate in candidates) f.Candidates.Add(NoteAt(candidate));
            return f;
        }
    }

    public sealed class NativeMemory : IMemory, IDisposable
    {
        private readonly SafeProcessHandle handle;
        // QUERY_INFORMATION is required by VirtualQueryEx; all access remains read/query-only.
        public NativeMemory(int pid)
        {
            handle = OpenProcess(0x0410, false, pid);
            if (handle.IsInvalid) { handle.Dispose(); throw new InvalidOperationException("process_read_access_denied"); }
        }
        public bool Alive { get { uint code; return GetExitCodeProcess(handle, out code) && code == 259; } }
        public long CreatedFileTime { get { long created, exited, kernel, user; return GetProcessTimes(handle, out created, out exited, out kernel, out user) ? created : 0; } }
        public uint? ExitCode { get { uint code; return GetExitCodeProcess(handle, out code) ? (uint?)code : null; } }
        public byte[] Read(uint address, int size)
        {
            if (address < 65536 || size < 1 || size > 65536 + 68 || (ulong)address + (uint)size > UInt32.MaxValue) return null;
            byte[] bytes = new byte[size]; UIntPtr read;
            return ReadProcessMemory(handle, new IntPtr((long)address), bytes, (UIntPtr)size, out read) &&
                read.ToUInt64() == (ulong)size ? bytes : null;
        }
        internal bool Region(ulong at, out RegionInfo region)
        { return VirtualQueryEx(handle, new IntPtr((long)at), out region, (UIntPtr)Marshal.SizeOf(typeof(RegionInfo))) != UIntPtr.Zero; }
        public void Dispose() { handle.Dispose(); }
        [StructLayout(LayoutKind.Sequential)]
        internal struct RegionInfo
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State, Protect, Type;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(SafeProcessHandle p, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(SafeProcessHandle p, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(SafeProcessHandle p, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(SafeProcessHandle p, IntPtr address, out RegionInfo info, UIntPtr size);
    }
    public sealed class ScanResult
    {
        public List<uint> Addresses = new List<uint>();
        public long BytesAttempted, ElapsedMs;
        public string Status;
    }
    public sealed class MarkerSearch : IDisposable
    {
        private CancellationTokenSource cancel = new CancellationTokenSource();
        public Task<ScanResult> Task;
        public MarkerSearch(int pid, long expectedCreatedFileTime)
        {
            Task = System.Threading.Tasks.Task.Run(delegate { return Run(pid, expectedCreatedFileTime); });
        }
        private ScanResult Run(int pid, long expectedCreatedFileTime)
        {
            ScanResult result = new ScanResult { Status = "complete" };
            Stopwatch time = Stopwatch.StartNew();
            try
            {
                using (NativeMemory memory = new NativeMemory(pid))
                {
                    if (expectedCreatedFileTime == 0 || memory.CreatedFileTime != expectedCreatedFileTime)
                    { result.Status = "process_changed"; return result; }
                    ulong at = 65536;
                    while (at < UInt32.MaxValue)
                    {
                        if (cancel.IsCancellationRequested) { result.Status = "cancelled"; break; }
                        if (time.ElapsedMilliseconds >= 20000 || result.BytesAttempted >= 512L * 1024 * 1024 || result.Addresses.Count >= 256)
                        { result.Status = "bounded_limit"; break; }
                        NativeMemory.RegionInfo region;
                        if (!memory.Region(at, out region)) break;
                        ulong start = (ulong)region.BaseAddress.ToInt64();
                        ulong end = Math.Min((ulong)UInt32.MaxValue, start + region.RegionSize.ToUInt64());
                        if (end <= at) break;
                        if (region.State == 0x1000 && (region.Protect & 0x101) == 0 && region.Type != 0x1000000)
                        {
                            for (ulong p = Math.Max(at, start); p + 0x48 <= end; p += 65536)
                            {
                                if (cancel.IsCancellationRequested || time.ElapsedMilliseconds >= 20000 ||
                                    result.BytesAttempted >= 512L * 1024 * 1024 || result.Addresses.Count >= 256) break;
                                int size = (int)Math.Min(Math.Min(65536 + 68, end - p), (ulong)(512L * 1024 * 1024 - result.BytesAttempted));
                                byte[] bytes = memory.Read((uint)p, size); result.BytesAttempted += size;
                                if (bytes != null) result.Addresses.AddRange(Find(bytes, (uint)p, 256 - result.Addresses.Count));
                                Thread.Sleep(1);
                            }
                        }
                        at = end;
                    }
                }
                if (cancel.IsCancellationRequested) result.Status = "cancelled";
                else if (time.ElapsedMilliseconds >= 20000 || result.BytesAttempted >= 512L * 1024 * 1024 || result.Addresses.Count >= 256)
                    result.Status = "bounded_limit";
            }
            catch { result.Status = "read_error"; }
            result.ElapsedMs = time.ElapsedMilliseconds; return result;
        }
        public static List<uint> Find(byte[] bytes, uint start, int limit)
        {
            List<uint> hits = new List<uint>();
            // Extra 68 bytes overlap chunks; each base is emitted by its first chunk only.
            for (int i = 0; i < Math.Min(65536, bytes.Length) && i + 0x48 <= bytes.Length && hits.Count < limit; i += 4)
            {
                if (BitConverter.ToInt32(bytes, i + 8) != 111000) continue;
                byte[] block = new byte[0x48]; Array.Copy(bytes, i, block, 0, block.Length);
                Note n = Probe.Decode(start + (uint)i, block, new Aliases());
                if (n.Status == "ok") hits.Add(start + (uint)i);
            }
            return hits;
        }
        public void Dispose() { cancel.Cancel(); /* Worker owns and closes its read handle. */ }
    }
}
