# Changelog

## [0.6.9-rocklist.2] - 2026-07-14

### Added

- Added diagnostic-only multiplayer evidence to each memory readout without
  changing the existing Sniffer state machine.
- Multiplayer readouts now sample both arrangement-ID pointer chains and report
  which candidates are structurally valid.
- Multiplayer readouts now report Learn a Song and Score Attack note-data
  validity and the selected note-data source for captured sessions.

### Changed

- This revision matches the RockSnifferLib source shipped with RockList
  Desktop 0.1.0-beta.19.

## [0.6.9-rocklist.1] - 2026-07-14

### Added

- Published the RockList-maintained baseline fork based on PoizenJam 0.6.9.
- Documented the library's lineage, build layout, contribution expectations,
  multiplayer status, upstream maintenance, and RockList vendor workflow.
- Added a Windows and .NET 8 build check using the PSARC library revision used
  by RockList Desktop.

### Included upstream work

- PoizenJam RockSnifferLib 0.6.9 lifecycle, pause, completion, arrangement, and
  multiplayer-state foundations.
