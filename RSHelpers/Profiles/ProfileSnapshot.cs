namespace RockSnifferLib.RSHelpers.Profiles;

/// <summary>A saved-profile identity supplied by the host, not an ID read from game memory.</summary>
public sealed record ProfileIdentity(string Id, string Name);

/// <summary>
/// Experimental observation of the initial profile login. An observed login is
/// inferred from selection followed by main, not a persistent active-profile read
/// and not multiplayer player-slot ownership.
/// </summary>
public sealed record ProfileSnapshot
{
    public bool Supported { get; init; }
    /// <summary>unknown, selecting, or observed.</summary>
    public string State { get; init; } = "unknown";
    public string? Stage { get; init; }
    /// <summary>Selection-screen text only; may not identify a saved profile.</summary>
    public string? SelectedProfileName { get; init; }
    /// <summary>Unique, exact match in the host's catalog, otherwise null.</summary>
    public ProfileIdentity? SelectedProfile { get; init; }
    /// <summary>
    /// Latest selection followed immediately by main during continuous observation.
    /// Cleared on title/selection, read failure, or a polling gap over two seconds.
    /// </summary>
    public ProfileIdentity? ObservedLoadedProfile { get; init; }
}
