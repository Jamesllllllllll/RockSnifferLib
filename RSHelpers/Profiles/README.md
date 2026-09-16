# Experimental profile observation

This contribution is based on RockBuddy's `rock-buddy` library branch and is
independent of the multiplayer contribution. It ports the profile observer
developed for the RockList-maintained library while retaining Buddy's numeric
modes, chart note counts, note-data hashes and existing defaults. The original
library's MIT license and credits are preserved.

## RockBuddy executable integration

After merging this library change, update RockSniffer's `RockSnifferLib`
submodule and rebuild. Its existing configuration loader and JSON serializer
can expose the feature without an executable rename, version-suffix change,
or HTTP response rewrite. Example `config/sniffer.json` additions:

```json
{
  "enableExperimentalProfiles": true,
  "experimentalProfileCatalog": [
    { "Id": "saved-profile-id", "Name": "Player One" }
  ]
}
```

Supply real IDs/names from the host's saved-profile metadata. This library
does not search the user's Steam files or choose an account. The catalog is a
snapshot at reader construction; recreate the reader after catalog changes.
Consume `memoryReadout.experimentalProfiles` even when the root `success`
flag is false: the existing flag describes song metadata, which may be absent
during startup. Start the sniffer before login. Updating RockBuddy's UI or its
selected save profile is left to the application; this observation is not
authentication or proof of multiplayer slot ownership.

`ExperimentalProfileReader` observes the highlighted profile during startup
and infers which profile loaded when that selection is followed by the main
menu. It is opt-in and read-only. Both `RSEdition.Remastered` and
`RSEdition.Remastered_Learn_And_Play` are available for experimental use.
Sniffer passes the same edition used by its single-player reader, without an
additional executable-hash check. The existing RockSniffer host's edition
detection is unchanged; standalone callers must identify and supply the edition.

Learn & Play has live validation. Remastered uses the single-player edition
mapping to move starting addresses down by `0x1000`, with unchanged pointer
steps. It has synthetic coverage and is available for community testing,
without a live compatibility claim.

This is **not a persistent active-profile pointer**. Start observing before
login and poll every 100–250 ms. Attaching after login returns unknown. The
snapshot does not identify multiplayer player slots.

## Standalone use

```csharp
using RockSnifferLib.RSHelpers.Profiles;

// Supply current saved-profile metadata from your application's local catalog.
// IDs are opaque to the reader; they are not read from game memory.
ProfileIdentity[] knownProfiles = savedProfiles
    .Select(p => new ProfileIdentity(p.Id, p.Name)).ToArray();
// Pass the edition identified by your host's existing Rocksmith detection.
using var reader = new ExperimentalProfileReader(rocksmithProcess, edition, knownProfiles);

while (!cancellationToken.IsCancellationRequested)
{
    ProfileSnapshot snapshot = reader.Read();
    // snapshot.SelectedProfileName: selection text, possibly not a saved profile.
    // snapshot.SelectedProfile: unique exact catalog match, or null.
    // snapshot.ObservedLoadedProfile: inferred login identity, or null.
    PublishSnapshot(snapshot);
    await Task.Delay(100, cancellationToken);
}
```

The reader copies the catalog when constructed. Dispose and recreate it for
every game process or catalog change. It owns a separate process handle with
only `VM_READ | QUERY_LIMITED_INFORMATION`, checks process creation time to
guard against PID reuse, and has no enumeration, save parsing, file watching,
network, or memory-write behavior. Constructor access errors may throw;
unmapped edition values produce `Supported = false` without memory reads.

## Normal Sniffer readouts

```csharp
var settings = new SnifferSettings
{
    enableExperimentalProfiles = true,
    experimentalProfileCatalog = knownProfiles,
};
var sniffer = new Sniffer(rocksmithProcess, cache, edition, settings);
sniffer.OnMemoryReadout += (_, args) =>
{
    ProfileSnapshot? profile = args.memoryReadout.experimentalProfiles;
    PublishSnapshot(profile);
};
```

This setting defaults to false; `experimentalProfiles` is then null. Enabling
it adds a snapshot without changing song events or multiplayer ownership.
The general Sniffer's existing auto-enumeration setting is independent of this
read-only feature. An empty catalog exposes selection text but never infers a
loaded identity. The host is responsible for loading and refreshing its own
profile metadata and clearing displayed identity when it detaches/stops.
Polling, publication and shutdown are synchronized so a completed read cannot
restore a profile after the cleared shutdown readout has been published.

## Snapshot semantics

`Supported` means a layout is available for the supplied edition; it does not
mean that this executable has passed live testing or that a profile was found.

| State | Meaning |
| --- | --- |
| `unknown` | No trustworthy login observation, or unmapped edition. Check `Supported` separately. |
| `selecting` | Selection UI stage; highlighted text and a unique catalog match may be available. No loaded identity. |
| `observed` | A uniquely matched selection was immediately followed by `main` during continuous polling. |

Matching is case-sensitive and exact, without whitespace or Unicode
normalization. Duplicate names or IDs, unrecognized text, partial reads,
malformed UTF-8, and unreadable pointers do not establish identity. IDs come
solely from the host's catalog. Do not treat a display name as a globally
unique identifier or use this observation as authentication.

The latest valid selection becomes `ObservedLoadedProfile` only on the direct
selection-to-`main` transition. A selection read failure replaces the candidate
with unknown. An intervening stage, such as title or loading, discards the
candidate. Missing/invalid stages, process exit, backwards time, or a polling
gap over two seconds clear history. The two-second bound is a conservative
observation policy, not a measured game timeout; keep polling through loading.
Returning to title, startup notifications, or selection clears a loaded
identity. Ordinary menus/gameplay retain the inferred initial login. That
retained value does not establish ownership of either multiplayer slot.

## Validation and limitations

On Learn & Play, the selection chain was observed in three game processes, across two complete
restarts, with two distinct profiles and user-confirmed highlights/logins.
Automatic login left roughly six seconds of readable selection data. The name
pointer disappeared just after `main`, so the implementation does not read it
as an active profile after login or guess from save timestamps.

The tested selection screen reported `panel_bib`; `profileselect` is also
recognized but was not the stage observed in those restart controls. The
Learn & Play selection name uses root RVA `0x00F6062C` and dereference-then-add offsets
`0x18, 0x3C, 0x28, 0x1FC`. Stage is a bounded ASCII read at RVA `0x00F607C9`.
Name reads are bounded, null-terminated UTF-8. Selection offsets/semantics were
located through RSMods' `CurrentSelectedUser` and independently observed:
[GameState.cpp](https://github.com/Lovrom8/RSMods/blob/1e4a67d43dfd0bae2b613edc4cd67b922332dc61/DLL/GameState.cpp),
[Offsets.cpp](https://github.com/Lovrom8/RSMods/blob/1e4a67d43dfd0bae2b613edc4cd67b922332dc61/DLL/Offsets.cpp).

Tests cover both edition layouts through synthetic selection/login sequences,
plus switching highlights, cancellation,
late attachment, restart isolation, interrupted reads, ambiguous catalogs,
UTF-8 validation, pointer bounds, disposal, and optional readout serialization.
A separate live late-attachment check of the implemented reader returned
`Supported = true`, stage `main`, and unknown identity on all ten samples,
as expected when attaching after login. The unit tests are not additional
live-game validations. Other builds/machines,
non-ASCII names in the game, profile creation/rename/deletion, unusual failed
loads, and multiplayer profile ownership need further observation. The stage
is an engine signal, not proof of the visible UI; unobserved transitions can
still make an inferred login inaccurate. Treat this API as experimental.
