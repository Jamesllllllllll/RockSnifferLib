# Contributing

Thank you for helping improve RockSnifferLib for the Rocksmith community.

## Good contributions

- A minimal description of the Rocksmith mode and transition that behaved
  unexpectedly.
- Reproduction steps that distinguish single-player, multiplayer, Nonstop
  Play, Score Attack, Learn a Song, menus, pause, restart, and results states.
- Tests or small diagnostic fixtures that do not contain personal data or
  copyrighted song-package contents.
- Focused fixes with an explanation of why the memory or state transition is
  reliable.
- Documentation and examples for applications using the library.

## Privacy and content

Do not submit memory dumps, complete PSARC/CDLC files, account information,
streaming credentials, local file paths containing personal information, or
private RockList diagnostic reports to a public issue.

If a problem needs sensitive diagnostic data, open an issue with a redacted
description first so the maintainers can arrange an appropriate review path.

## Pull requests

1. Branch from the current `master` branch.
2. Keep changes limited to one behavior or maintenance goal.
3. Preserve the existing MIT license and relevant discovery credits.
4. Build the solution against .NET 8 and the pinned
   Rocksmith2014PsarcLib-compatible layout.
5. Describe the Rocksmith modes and transitions you validated.

Multiplayer behavior should remain explicitly marked experimental until it has
been observed across enough sessions and game states to be dependable.
