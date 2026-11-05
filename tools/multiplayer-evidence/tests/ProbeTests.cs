using System;
using System.Collections.Generic;
using System.Text;
using RockListEvidence;

public sealed class FixtureMemory : IMemory
{
    public Dictionary<uint, byte> Data = new Dictionary<uint, byte>();
    public int Reads;
    private uint allocation = 0x3000000;
    public void Put(uint a, byte[] b) { for (int i = 0; i < b.Length; i++) Data[a + (uint)i] = b[i]; }
    public byte[] Read(uint a, int size)
    {
        Reads++; if (size > 128) throw new Exception("unbounded probe read");
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++) if (!Data.TryGetValue(a + (uint)i, out bytes[i])) return null;
        return bytes;
    }
    public void Chain(uint root, uint target, params uint[] offsets)
    {
        uint a = 0x400000 + root;
        for (int i = 0; i < offsets.Length; i++)
        {
            byte[] prior = Read(a, 4);
            uint next = i == offsets.Length - 1 ? target - offsets[i] : prior != null ? BitConverter.ToUInt32(prior, 0) : allocation += 0x1000;
            Put(a, BitConverter.GetBytes(next)); a = next + offsets[i];
        }
    }
}
public static class ProbeTests
{
    private static int count;
    private static void Check(bool b, string message) { if (!b) throw new Exception(message); count++; }
    public static byte[] Block(int hits, int misses)
    {
        byte[] b = new byte[0x48]; BitConverter.GetBytes(111000).CopyTo(b, 8);
        new Guid("11111111-2222-3333-4444-555555555555").ToByteArray().CopyTo(b, 0x20);
        BitConverter.GetBytes(hits).CopyTo(b, 0x30); BitConverter.GetBytes(misses).CopyTo(b, 0x40);
        return b;
    }
    public static FixtureMemory Ready(uint root)
    {
        FixtureMemory m = new FixtureMemory();
        m.Chain(root, 0x5000000, 0xB0, 0x30, 0x18, 4, 0x84, 0);
        m.Chain(root - 0xA4, 0x5000000, 0x134, 0x88, 0x28, 0, 0x24, 0);
        m.Chain(root, 0x5100000, 0xB0, 0x34, 0x18, 4, 0x74, 0);
        m.Chain(root - 0xA4, 0x5100000, 0x134, 0x88, 0x28, 4, 0x24, 0);
        m.Chain(root, 0x5200000, 0xB0, 0x30, 0x538, 8);
        m.Chain(root, 0x5200100, 0xB0, 0x34, 0x538, 8);
        m.Chain(root, 0x5200200, 0xB0, 0x538, 8);
        m.Chain(root - 0x118, 0x5300000, 0xBC, 0);
        m.Put(0x5000000, Block(80, 20)); m.Put(0x5100000, Block(147, 49));
        m.Put(0x5200000, BitConverter.GetBytes(4f)); m.Put(0x5200100, BitConverter.GetBytes(4f));
        m.Put(0x5200200, BitConverter.GetBytes(0f)); m.Put(0x400000 + root - 0x30, new byte[] { 2 });
        byte[] stage = new byte[64]; Encoding.ASCII.GetBytes("panel_bib").CopyTo(stage, 0);
        m.Put(0x400000 + root + 0x19D, stage);
        byte[] song = new byte[128]; Encoding.ASCII.GetBytes("Play_private_name_Preview").CopyTo(song, 0); m.Put(0x5300000, song);
        return m;
    }
    public static Frame Sample()
    { return new Probe(Ready(0xF5F62C), 0x400000, 0xF5F62C, new Aliases()).Capture(null); }
    public static int Run()
    {
        count = 0;
        foreach (uint root in new uint[] { 0xF5F62C, 0xF6062C })
        {
            FixtureMemory m = Ready(root); Aliases aliases = new Aliases();
            Probe p = new Probe(m, 0x400000, root, aliases); Frame f = p.Capture(null);
            Check(f.DistinctPlayers, "distinct players"); Check(f.P1Status == "ok" && f.P2Status == "ok", "path agreement");
            Check(f.P1Primary.Accuracy == 80 && f.P2Primary.Accuracy == 75, "accuracy");
            Check(f.P1Primary.Arrangement == f.P2Primary.Arrangement, "equal GUIDs allowed");
            Check(f.P1Primary.Arrangement != "11111111222233334444555555555555", "GUID pseudonymization");
            Check(f.Song != "private_name" && f.Song.StartsWith("song-"), "song pseudonymization");
            Check(f.TimersAgree && f.Timer1 == 4 && f.Pause == 2 && f.Stage == "panel_bib", "timing fields");
            Check(f.Walks[0].Hops.Count == 6 && f.Walks[8].Hops.Count == 0, "walk direction/direct roots");
            m.Put(0x400000 + root - 0xA4, new byte[4]); f = p.Capture(null);
            Check(f.P1Primary.Status == "ok" && f.P1Secondary.Status == "unreadable" && !f.DistinctPlayers, "keep raw working primary on failed secondary");
            m = Ready(root); p = new Probe(m, 0x400000, root, aliases);
            m.Chain(root, 0x5000000, 0xB0, 0x34, 0x18, 4, 0x74, 0);
            m.Chain(root - 0xA4, 0x5000000, 0x134, 0x88, 0x28, 4, 0x24, 0);
            f = p.Capture(null); Check(!f.DistinctPlayers && f.P2Status == "ok", "ending alias rejected");
            m = Ready(root); p = new Probe(m, 0x400000, root, aliases);
            m.Put(0x5200000, BitConverter.GetBytes(Single.NaN)); f = p.Capture(null);
            Check(f.Timer1 == null && !f.TimersAgree, "NaN not serialized");
            m.Put(0x5200000, BitConverter.GetBytes(9f)); f = p.Capture(null); Check(!f.TimersAgree, "timer disagreement");
            byte[] secret = new byte[64]; Encoding.ASCII.GetBytes("C:\\Users\\PRIVATE-PERSON\\token-secret").CopyTo(secret, 0);
            m.Put(0x400000 + root + 0x19D, secret); f = p.Capture(null);
            Check(f.Stage == null && f.StageStatus == "unrecognized_text", "unexpected text not exported");
            Check(p.Follow("bad", root, UInt32.MaxValue).Status == "invalid_pointer", "overflow rejected");
            Check(p.Capture(new uint[] { 0x5000000 }).Candidates.Count == 1, "bounded candidate sampling");
        }
        Aliases a = new Aliases();
        Check(Probe.Decode(0x5000000, null, a).Status == "unreadable", "read failure");
        Check(Probe.Decode(0x5000000, new byte[0x48], a).Status == "wrong_marker", "marker required");
        Check(Probe.Decode(0x5000000, Block(0, 0), a).Accuracy == null, "zero notes unknown accuracy");
        Check(Probe.Decode(0x5000000, Block(-1, 5), a).Status == "invalid_counters", "negative counts rejected");
        byte[] chunk = new byte[65536 + 68]; Block(1, 2).CopyTo(chunk, 65532);
        var found = MarkerSearch.Find(chunk, 0x5000000, 256);
        Check(found.Count == 1 && found[0] == 0x5000000 + 65532, "cross-chunk structure found once");
        Check(MarkerSearch.Find(chunk, 0x5000000, 0).Count == 0, "candidate cap");
        return count;
    }
}
