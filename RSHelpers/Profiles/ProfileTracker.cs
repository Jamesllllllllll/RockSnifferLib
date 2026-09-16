using System;
using System.Collections.Generic;
using System.Linq;

namespace RockSnifferLib.RSHelpers.Profiles;

internal sealed class ProfileTracker
{
    private readonly Dictionary<string, ProfileIdentity> profiles;
    private double? lastAt;
    private string? previousStage;
    private ProfileIdentity? candidate;
    private ProfileIdentity? loaded;

    internal ProfileTracker(IEnumerable<ProfileIdentity> knownProfiles)
    {
        ArgumentNullException.ThrowIfNull(knownProfiles);
        var catalog = knownProfiles.ToArray();
        if (catalog.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name)))
            throw new ArgumentException("Profiles must have a nonempty ID and name.", nameof(knownProfiles));
        // Neither a duplicate display name nor an ID assigned to multiple names
        // establishes unique identity. Do not normalize case, whitespace or Unicode.
        var uniqueIds = catalog.GroupBy(p => p.Id, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        profiles = catalog.GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1 && uniqueIds.Contains(g.First().Id))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    internal static bool IsSelection(string? stage) => stage is "panel_bib" or "profileselect";

    internal ProfileSnapshot Update(double at, string? stage, string? selectedName)
    {
        if (!double.IsFinite(at) || at < 0 || string.IsNullOrEmpty(stage))
        {
            Reset();
            return new ProfileSnapshot { Supported = true };
        }
        if (lastAt.HasValue && (at < lastAt || at - lastAt > 2)) Reset();
        lastAt = at;

        ProfileIdentity? selected = null;
        if (IsSelection(stage))
        {
            loaded = null;
            if (selectedName != null) profiles.TryGetValue(selectedName, out selected);
            candidate = selected; // An unreadable/ambiguous selection replaces the old candidate.
        }
        else
        {
            selectedName = null;
            if (stage == "main" && IsSelection(previousStage)) loaded = candidate;
            if (stage is "titlescreen" or "Notifications") loaded = null;
            candidate = null; // Only the observed direct selection -> main transition qualifies.
        }
        previousStage = stage;
        return new ProfileSnapshot
        {
            Supported = true,
            Stage = stage,
            State = IsSelection(stage) ? "selecting" : loaded != null ? "observed" : "unknown",
            SelectedProfileName = selectedName,
            SelectedProfile = selected,
            ObservedLoadedProfile = loaded,
        };
    }

    private void Reset()
    {
        lastAt = null;
        previousStage = null;
        candidate = loaded = null;
    }
}
