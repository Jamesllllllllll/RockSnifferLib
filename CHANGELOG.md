# Changelog

## [0.6.9-rocklist.8] - 2026-08-06

### Fixed

- An unrecognized selected song now clears the previous song details instead
  of leaving applications with a stale title.
- Song-start events now require the resolved song details to match Rocksmith's
  current song ID.

## [0.6.9-rocklist.7] - 2026-08-05

### Added

- Added a reusable, process-independent PSARC library indexer for applications
  that manage multiple local song folders.
- Library scans reuse unchanged local records, isolate missing folders and
  unreadable files, avoid linked-directory loops, and support cancellation and
  bounded parallel work.

## [0.6.9-rocklist.6] - 2026-07-31

### Fixed

- Stopping or replacing a reader now cancels its pending song-library scan so
  old scans do not continue consuming disk and processor time in the
  background.

## [0.6.9-rocklist.5] - 2026-07-29

### Added

- Added privacy-safe runtime diagnostics for memory reads, song resolution, and
  song-library scanning so host applications can detect and recover from
  stalled readers.

## [0.6.9-rocklist.4] - 2026-07-28

### Fixed

- PSARC scanning now waits for downloads to finish before reading them and no
  longer prevents browsers from replacing temporary download files.

## [0.6.9-rocklist.3] - 2026-07-17

### Added

- Added diagnostic candidates for the community-reported multiplayer song
  timer pointer in both possible offset orders.

### Fixed

- Multiplayer diagnostic values now refresh on every readout instead of
  becoming stale when the ordinary song timer is unavailable.

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
