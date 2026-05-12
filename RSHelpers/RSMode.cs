using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockSnifferLib.RSHelpers
{
    /// <summary>
    /// Classification of the user's current Rocksmith mode-context.
    ///
    /// Pre-v0.6.8: this enum was set only when a note-data pointer chain
    /// resolved (LEARNASONG / SCOREATTACK), so menu states and Nonstop Play
    /// were misclassified (Nonstop reuses the LaS note-data subsystem
    /// internally, so it appeared as LEARNASONG; menus appeared as UNKNOWN
    /// because no note-data pointer resolves in them).
    ///
    /// v0.6.8: mode is derived from `gameStage` (via DeriveModeFromGameStage
    /// in RSMemoryReader), which is reliable across all states including
    /// menus, song-select, song-review, transitions, and Nonstop Play.
    /// The note-data pointer reads still happen for the note data itself,
    /// but no longer write `mode`.
    ///
    /// Integer values 0..3 are preserved from the pre-v0.6.8 enum for any
    /// external consumer doing integer-based serialization. New values
    /// (NONSTOPPLAY onwards) are appended.
    /// </summary>
    public enum RSMode
    {
        UNKNOWN,        // 0 - default / unrecognized gameStage
        LEARNASONG,     // 1 - learnasong, las_*, las_pause, las_songreview
        SCOREATTACK,    // 2 - scoreattack, sa_*, panel_bib, scoreattack_presongtuner
        MULTIPLAYER,    // 3 - mp_*, duet_*, h2h_*, split_game (full multiplayer support TBD)
        NONSTOPPLAY,    // 4 - nonstopplay, nsp_*, nonstopplayhub, nonstopplaygame
        GUITARCADE,     // 5 - gcpre, gcade, gcade_game, guitarcade_tuner, gc_*
        SESSION,        // 6 - sm_* (Session Mode)
        LESSONS,        // 7 - ge_*, getuner, pregametuner
        MENU            // 8 - titlescreen, profileselect, main, mainmenu, statsmenu,
                        //     shop, contentpanelchord, sidelist, tonedesigner*
    }
}
