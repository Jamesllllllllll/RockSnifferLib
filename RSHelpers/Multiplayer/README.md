# Experimental multiplayer API

This contribution is based on RockBuddy's `rock-buddy` library branch. It ports
the experimental reader from [the RockList-maintained library](https://github.com/Jamesllllllllll/RockSnifferLib/tree/v0.6.9-rocklist.14/RSHelpers/Multiplayer)
without changing Buddy's chart note counts, note-data hashes, numeric modes,
or existing configuration defaults. The original library's MIT license and
credits are preserved.

## RockBuddy executable integration

After merging this library change, update RockSniffer's `RockSnifferLib`
submodule and rebuild normally. No executable rename, version-suffix change,
or HTTP response rewrite is needed. Enable `enableExperimentalMultiplayer`
in `config/sniffer.json`; it defaults to false. RockSniffer's existing JSON
serializer then publishes `memoryReadout.experimentalMultiplayer`.

Treat this snapshot independently of the root `success` flag, which describes
the legacy song metadata, and independently of legacy single-player scores.
While an experimental multiplayer run is active, legacy `songTimer` and
`noteData` are zeroed and `mode` remains the existing numeric `MULTIPLAYER`
value (3). The host's legacy state returns to menus without emitting
single-player score-start/end events for that multiplayer snapshot. Player
statistics, timing and results are in the additional snapshot. No RockBuddy
app, backend, score submission, or profile-ownership policy is implemented here.

Both `RSEdition.Remastered` and `RSEdition.Remastered_Learn_And_Play` are
available for experimental use. Sniffer passes the same edition used by its
single-player reader; there is no additional executable-hash check here.
The existing RockSniffer host's edition detection is unchanged. Standalone
callers must identify the edition and pass it explicitly.

The starting addresses follow the existing single-player edition mapping;
Remastered uses addresses `0x1000` below Learn & Play with the same subsequent
pointer steps. Learn & Play has live validation; Remastered has synthetic
coverage and is available for community testing, without a live compatibility
claim. Other edition values return `Supported = false` without memory reads.

## Standalone read-only use

```csharp
using RockSnifferLib.RSHelpers.Multiplayer;

// Select the live Rocksmith process with its main window, not a leftover child.
// Pass the edition identified by your host's existing Rocksmith detection.
using var reader = new ExperimentalMultiplayerReader(rocksmithProcess, edition);
var snapshot = reader.Read((selectedSongId, arrangementIds) =>
    ResolveFromYourCatalog(selectedSongId, arrangementIds));
if (snapshot.Player2?.Accuracy is double accuracy)
    Console.WriteLine($"P2 accuracy: {accuracy:F1}%");
```

Poll every 100–200 ms. A UI can consume the most recent immutable snapshot less
frequently. Dispose the reader when detaching. It opens only VM_READ and
QUERY_LIMITED_INFORMATION access, reads bounded fields, and never enumerates,
writes, freezes, debugs or dumps the process. This guarantee is for this reader,
not the separate legacy enumeration API.

The optional catalog callback receives the last observed preview song ID and
both current arrangement GUIDs. Return null when unresolved or ambiguous.
Both GUIDs must belong to the returned song before it supplies song identity,
duration, path or tuning. Late attachment without a preview can still supply
stats and elapsed time; a host that resolves GUIDs can also supply metadata.

## Use through Sniffer

```csharp
var settings = new RockSnifferLib.Configuration.SnifferSettings {
    enableAutoEnumeration = false,
    enableExperimentalMultiplayer = true
};
var sniffer = new RockSnifferLib.Sniffing.Sniffer(process, cache, edition, settings);
sniffer.OnMemoryReadout += (_, args) => {
    var multiplayer = args.memoryReadout.experimentalMultiplayer;
    // Consume multiplayer separately from legacy song events.
};
```

Sniffer uses its cache and the observed preview ID for metadata lookup. If it
attached after the preview disappeared, metadata may remain unavailable until
the next selection. The standalone callback supports host-specific GUID lookup.
The feature defaults off and does not redefine legacy single-player fields or
emit multiplayer completion through legacy events. When enabled, ordinary
timer/note fields are cleared during an experimental multiplayer run so their
invalid values cannot enter the legacy state machine.
The legacy pointer read is skipped during such runs. Polling, publication and
shutdown are synchronized so stopping cannot publish a previously captured
snapshot after the cleared shutdown readout.

## Snapshot contract

`Supported` means a layout is available for the supplied edition; it does not
mean that this executable has passed live testing or that a session was found.

- `inactive`: no current multiplayer session; retained `LastResult` may exist.
- `loading`: pointers exist but advancing playback has not yet been established.
- `playing` / `paused`: shared timer evidence and pause byte agree with that state.
- `unavailable`: temporary read gap; live timing is unavailable.
- `ended`: sustained pointer loss; consult `LastResult` for retained values.

`RunId` changes on a new observed run, arrangement change or counter reset.
`StartedBeforeObservation` means attachment/reacquisition did not observe the
start. A restart with no counted notes cannot always be distinguished from the
normal resume rewind. Nonzero counter resets mark a discontinuity; the reader
does not add discarded counts back into the new run.

Each slot is independently nullable. Both paths must agree on that slot's
address and repeated counter reads; shared addresses are rejected. Equal
arrangement GUIDs are allowed. Do not infer player identity from an address or
arrangement: addresses can be reused by the other slot in the next song.

`Accuracy` is `100 * Hits / (Hits + Misses)`, null before any counted notes.
It is independent of difficulty and is not mastery. Validation observed P2
147/196 = 75% and P1 865/1244 = 69.533762%, with longest streak 66. The user
reported 75%, 69%, and 66 respectively. Exact game display rounding is not
verified; hosts choose their display precision. Reports were user observations,
not saved results images.

`LastResult` preserves the last agreed pair before teardown. Outcomes are
`completed` (inferred from advancing time reaching resolved metadata duration
before unload), `abandoned` (leaving the gameplay context early), or `unknown`.
Do not use these experimental outcomes for irreversible completion side effects.
Process loss must be treated as unknown by the host; dispose and create a new
reader for a new process. No mastery, score, chart-wide note total or precise
start timestamp is fabricated.

## Evidence and limits

On Learn & Play, timer/GUID walks survived two complete game restarts with reciprocal ownership,
swapped arrangements, identical arrangements, a second song, pause-tuner,
same-song restart, early exit, natural endings and return to single-player.
Alternate/bonus arrangements and other untested submenus remain experimental.
The test project uses compact synthetic timer, pointer and counter sequences.
Both edition layouts are exercised, including preview, stage, pause, both
players' counters and timers. These tests do not replace live Remastered checks.
Result-retention tests cover loss of either player's data and ensure that a
newer partial read cannot overwrite the last complete pair. Recorded gameplay
files are not required to build or test the library.

The counter-marker/field layout builds on RockSnifferLib's existing Learn-a-Song
reader. New multiplayer walks were established through read-only Windows tests
and restart validation; debugging/write tracing was not used.
