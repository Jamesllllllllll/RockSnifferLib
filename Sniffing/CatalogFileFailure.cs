using System.Collections.Generic;

namespace RockSnifferLib.Sniffing
{
    /// <summary>
    /// A local-only description of a PSARC file that could not be inspected.
    /// Host applications must not upload filePath without explicit user consent.
    /// </summary>
    public sealed class CatalogFileFailure
    {
        public string fileName { get; init; } = string.Empty;
        public string filePath { get; init; } = string.Empty;
        public string reason { get; init; } = CatalogFileFailureReasons.ReadFailed;
        public string message { get; init; } = string.Empty;
    }

    public static class CatalogFileFailureReasons
    {
        public const string NotReady = "not_ready";
        public const string HashFailed = "hash_failed";
        public const string ChangedDuringScan = "changed_during_scan";
        public const string ReadFailed = "read_failed";
        public const string QueueFailed = "queue_failed";

        public static readonly IReadOnlyList<string> All = new[]
        {
            NotReady,
            HashFailed,
            ChangedDuringScan,
            ReadFailed,
            QueueFailed,
        };

        public static string GetMessage(string reason)
        {
            return reason switch
            {
                NotReady => "The file was not complete and stable when RockList checked it.",
                HashFailed => "RockList could not verify the file.",
                ChangedDuringScan => "The file changed while RockList was checking it.",
                QueueFailed => "RockList could not schedule the file for inspection.",
                _ => "RockList could not read the file.",
            };
        }
    }
}
