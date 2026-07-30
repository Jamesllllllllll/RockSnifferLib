namespace RockSnifferLib.Sniffing
{
    /// <summary>
    /// Privacy-safe runtime health counters for a Sniffer instance.
    /// </summary>
    public sealed class SnifferDiagnosticsSnapshot
    {
        public long memoryReadAttempts { get; init; }
        public long successfulMemoryReads { get; init; }
        public long failedMemoryReads { get; init; }
        public string lastMemoryReadErrorType { get; init; }
        public int catalogFilesDiscovered { get; init; }
        public int catalogFilesProcessed { get; init; }
        public int catalogFilesFailed { get; init; }
        public int catalogSongsLoaded { get; init; }
        public bool catalogScanComplete { get; init; }
        public bool selectedSongDetected { get; init; }
        public bool selectedSongResolved { get; init; }
    }
}
