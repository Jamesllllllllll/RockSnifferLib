# Collector development handoff

This branch adds a standalone Windows evidence collector under
`tools/multiplayer-evidence`. It has not been merged or released. The latest
local package is `0.1.0-candidate.7`; use `Build-Package.ps1` to recreate it.

## Current state

- The wizard has 39 checkpoints, per-step progress, revisitable attempts, explicit
  step selection, contextual counter searches, and required results entry with a
  skip option. Either player can use guitar or bass; one person can alternate.
- Memory access is read/query-only. No game writes, debugging, dumps, injection,
  automatic uploads or startup registration are included.
- Exports use an explicit file allowlist, aliases for song/arrangement identities,
  relative timestamps and verified hashes. Interrupted captures can be recovered.
- Tests exercise synthetic memory layouts, privacy, exports/recovery, numeric
  entry, step navigation, repeat isolation, stopped recording and save retries.
- Demo and extracted-package smoke checks passed. A live gameplay pilot and
  visual checks at different Windows display scales remain outstanding.
- The library project excludes the standalone C# helper and its tests from its
  compilation. Production reader behavior is unchanged by this branch.

## Reconcile the newer reader before further discovery

The collector was developed against `72e652a4725d8af8ba7d0c63524ab0feb490cfa5`.
Its layout reference and probe deliberately still identify that revision.

As checked on November 5, 2026, public `master` has advanced to `754261a`,
including arrangement recovery (`#17`) and Remastered multiplayer/metadata
fixes (`#18`). Review those changes before treating the collector's candidate
addresses or limitations as current reader behavior. Update probes and their
fixtures together if needed, retaining exact source/build provenance in returned
evidence. Do not silently reinterpret old captures using new offsets.

## Continue development

From the repository root on Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/multiplayer-evidence/tests/Test.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tools/multiplayer-evidence/Collector.ps1 -Demo -SmokeTest -WorkflowTest
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tools/multiplayer-evidence/Collector.ps1 -Demo
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/multiplayer-evidence/Build-Package.ps1 -Destination "$env:USERPROFILE\Downloads\collector-review.zip"
```

Use a new package filename; the builder refuses to overwrite an existing ZIP.
The Windows collector cannot run on Linux, but its source and documentation can
be edited there. Keep game tests and evidence on the machine running Rocksmith.

Keep tester instructions short and action-oriented. Do not add explanatory UI
text when a disabled, contextual or clearly named control can guide the action.
Keep raw captures and machine-specific monitoring outside this repository.
