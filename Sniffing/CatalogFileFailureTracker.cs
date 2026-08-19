using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RockSnifferLib.Sniffing
{
    internal sealed class CatalogFileFailureTracker
    {
        internal const int MaxReportedFailures = 100;

        private readonly Dictionary<string, CatalogFileFailure> failures =
            new Dictionary<string, CatalogFileFailure>(StringComparer.OrdinalIgnoreCase);

        internal int Count => failures.Count;
        internal bool IsTruncated => failures.Count > MaxReportedFailures;

        internal void RecordFailure(string filePath, string reason)
        {
            var safePath = filePath ?? string.Empty;
            failures[safePath] = new CatalogFileFailure
            {
                fileName = Path.GetFileName(safePath),
                filePath = safePath,
                reason = reason,
                message = CatalogFileFailureReasons.GetMessage(reason),
            };
        }

        internal void RecordSuccess(string filePath)
        {
            failures.Remove(filePath ?? string.Empty);
        }

        internal void Clear()
        {
            failures.Clear();
        }

        internal IReadOnlyList<CatalogFileFailure> GetFailures()
        {
            return failures.Values
                .OrderBy(failure => failure.fileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(failure => failure.filePath, StringComparer.OrdinalIgnoreCase)
                .Take(MaxReportedFailures)
                .ToArray();
        }

        internal IReadOnlyDictionary<string, int> GetReasonCounts()
        {
            return failures.Values
                .GroupBy(failure => failure.reason, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }
    }
}
