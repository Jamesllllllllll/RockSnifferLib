using System;

namespace RockSnifferLib.RSHelpers.Multiplayer;

/// <summary>Pure reducer; loss of a pointer is not proof of completion.</summary>
internal sealed class MultiplayerTracker
{
    private MultiplayerSnapshot current = new() { Supported = true };
    private MultiplayerFrame? previous;
    private double? missingSince;
    private PlayerSnapshot? last1, last2;
    private double? lastTimer;
    private bool moved;

    internal MultiplayerSnapshot Update(MultiplayerFrame f)
    {
        var p1 = f.Player1;
        var p2 = f.Player2;
        if (p1 != null && p2 != null && p1.Address == p2.Address)
            p1 = p2 = null;
        bool valid = f.Pause is >= 0 and <= 2 && p1 != null && p2 != null && Plausible(f.Timer1, f.Duration)
            && Plausible(f.Timer2, f.Duration) && Math.Abs(f.Timer1!.Value - f.Timer2!.Value) <= .05;
        bool outside = IsOutside(f.Stage);
        if (!valid || outside)
        {
            if (current.RunId != null && current.State != "ended")
            {
                missingSince ??= f.At;
                if (outside || f.At - missingSince >= .6)
                {
                    bool complete = moved && !current.HasDiscontinuity && current.DurationSeconds is > 0
                        && lastTimer >= current.DurationSeconds - .25;
                    var result = new MultiplayerResult(current.RunId,
                        complete ? "completed" : outside ? "abandoned" : "unknown",
                        complete ? "duration_reached_then_unloaded" : outside ? "left_gameplay" : "data_unavailable",
                        lastTimer, last1, last2, current.HasDiscontinuity);
                    current = current with { State = "ended", TimerReliable = false,
                        ElapsedSeconds = null, Player1 = null, Player2 = null, LastResult = result };
                }
                else current = current with { State = "unavailable", TimerReliable = false,
                    ElapsedSeconds = null, Player1 = p1?.Stats, Player2 = p2?.Stats };
            }
            // Do not keep a multiplayer display alive after switching to single-player.
            if (outside) current = current with { State = "inactive", RunId = null,
                SongId = null, DurationSeconds = null, Player1 = null, Player2 = null,
                ElapsedSeconds = null, TimerReliable = false };
            current = current with { Stage = f.Stage };
            previous = null;
            return current;
        }

        double timer = Math.Min(f.Timer1!.Value, f.Timer2!.Value);
        bool identityChanged = last1 != null && last2 != null &&
            (last1.ArrangementId != p1!.Stats.ArrangementId || last2.ArrangementId != p2!.Stats.ArrangementId);
        bool countersFell = last1 != null && last2 != null &&
            (p1!.Stats.Hits < last1.Hits || p1.Stats.Misses < last1.Misses ||
             p2!.Stats.Hits < last2.Hits || p2.Stats.Misses < last2.Misses);
        // Resume can rewind below one second before the pause byte clears.
        // A timer rewind alone cannot distinguish that from a zero-note restart.
        bool reset = current.RunId != null && current.State != "ended" && countersFell;
        bool newRun = current.RunId == null || current.State == "ended" || identityChanged || reset;
        if (newRun)
        {
            var result = current.LastResult;
            if (current.RunId != null && current.State != "ended")
                result = new MultiplayerResult(current.RunId, "unknown",
                    reset ? "counter_or_timer_reset" : "arrangement_changed",
                    lastTimer, last1, last2, true);
            current = new MultiplayerSnapshot { Supported = true, RunId = Guid.NewGuid().ToString("N"),
                State = "loading", StartedBeforeObservation = timer > 1,
                HasDiscontinuity = reset && timer >= 1, LastResult = result };
            previous = null;
            moved = false;
        }
        bool advancing = previous?.Timer1 is double old && timer > old + .001;
        moved |= advancing;
        bool rewound = previous?.Timer1 is double before && timer < before - .1;
        string state = f.Pause is > 0 ? "paused" : advancing ? "playing" :
            current.State == "playing" && !rewound ? "playing" : "loading";
        current = current with { State = state, Stage = f.Stage, SongId = f.SongId,
            DurationSeconds = f.Duration, ElapsedSeconds = timer, TimerReliable = moved || f.Pause is > 0,
            Player1 = p1!.Stats, Player2 = p2!.Stats };
        last1 = p1.Stats; last2 = p2.Stats; lastTimer = timer;
        previous = f; missingSince = null;
        return current;
    }

    private static bool Plausible(double? time, double? duration) => time is double t
        && double.IsFinite(t) && t >= 0 && (t == 0 || t >= .001)
        && t <= (duration is > 0 ? duration.Value + 2 : 24 * 60 * 60);

    private static bool IsOutside(string? stage) => stage != null && stage != "split_game"
        && stage != "panel_bib" && stage != "las_pause" && stage != "las_tuner_ingame"
        && stage != "tuner";
}
