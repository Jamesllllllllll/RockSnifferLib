using System;
using System.Collections.Generic;

namespace RockSnifferLib.RSHelpers.Multiplayer;

/// <summary>Experimental, independent of the legacy single-player events.</summary>
public sealed record MultiplayerSnapshot
{
    public bool Supported { get; init; }
    public string State { get; init; } = "inactive";
    public string? RunId { get; init; }
    public string? SongId { get; init; }
    public double? ElapsedSeconds { get; init; }
    public double? DurationSeconds { get; init; }
    public bool TimerReliable { get; init; }
    public bool StartedBeforeObservation { get; init; }
    public bool HasDiscontinuity { get; init; }
    public string? Stage { get; init; }
    public PlayerSnapshot? Player1 { get; init; }
    public PlayerSnapshot? Player2 { get; init; }
    public MultiplayerResult? LastResult { get; init; }
}

public sealed record PlayerSnapshot(int Slot, string ArrangementId, int Hits,
    int Misses, int CurrentHitStreak, int HighestHitStreak, int CurrentMissStreak)
{
    public long ObservedNotes => (long)Hits + Misses;
    public double? Accuracy => ObservedNotes > 0 ? 100.0 * Hits / ObservedNotes : null;
    public string? Path { get; init; }
    public string? Tuning { get; init; }
}

/// <summary>Retained final values; never mistake these for a live player.</summary>
public sealed record MultiplayerResult(string RunId, string Outcome,
    string Reason, double? ElapsedSeconds, PlayerSnapshot? Player1,
    PlayerSnapshot? Player2, bool HasDiscontinuity);

// Addresses are diagnostic implementation details, never public player identity.
internal sealed record PlayerRead(uint Address, PlayerSnapshot Stats);
internal sealed record MultiplayerFrame(double At, string? Stage, int? Pause,
    double? Timer1, double? Timer2, PlayerRead? Player1, PlayerRead? Player2,
    string? SongId = null, double? Duration = null);
