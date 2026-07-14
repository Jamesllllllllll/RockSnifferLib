# RockSnifferLib

[![Build](https://github.com/Jamesllllllllll/RockSnifferLib/actions/workflows/build.yml/badge.svg)](https://github.com/Jamesllllllllll/RockSnifferLib/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

RockSnifferLib reads Rocksmith 2014 process and song-package data and exposes
song, arrangement, performance, pause, and lifecycle information to .NET
applications.

This is the RockList-maintained public fork used by
[RockList Desktop](https://rocklist.live). Its current focus is reliable
Rocksmith 2014 Remastered lifecycle detection and expanding multiplayer
support in a form that other community applications can reuse.

## Project status

- Windows and Rocksmith 2014 Remastered are the primary supported environment.
- Single-player song lifecycle and performance data are in active use by
  RockList Desktop.
- Multiplayer support is experimental and will expand as more session data is
  validated.
- The public API can change while the RockList-specific release series remains
  pre-1.0.

## Lineage and credits

This repository preserves the history and MIT license of the original
[kokolihapihvi/RockSnifferLib](https://github.com/kokolihapihvi/RockSnifferLib).
It builds on the actively developed
[PoizenJam/RockSnifferLib](https://github.com/PoizenJam/RockSnifferLib) fork,
whose lifecycle, pause, arrangement, and multiplayer-related work forms the
base currently used by RockList.

RockSnifferLib also depends on the MIT-licensed
[Rocksmith2014PsarcLib](https://github.com/kokolihapihvi/Rocksmith2014PsarcLib).
The original copyright notices remain in [LICENSE](LICENSE).

## Build

RockSnifferLib currently expects Rocksmith2014PsarcLib in a sibling directory,
matching the layout used by RockSniffer and RockList Desktop.

```text
rocksmith-libraries/
├── RockSnifferLib/
└── Rocksmith2014PsarcLib/
```

With the .NET 8 SDK installed:

```bash
mkdir rocksmith-libraries
cd rocksmith-libraries
git clone https://github.com/Jamesllllllllll/RockSnifferLib.git
git clone https://github.com/kokolihapihvi/Rocksmith2014PsarcLib.git
dotnet build RockSnifferLib/RockSnifferLib.sln --configuration Release
```

The build workflow uses the same layout and pins the PSARC dependency revision
used by RockList Desktop.

## Use in another .NET application

Add the library as a project reference from your application:

```xml
<ItemGroup>
  <ProjectReference Include="..\RockSnifferLib\RockSnifferLib.csproj" />
</ItemGroup>
```

Applications create a `Sniffer`, subscribe to the events they need, and start
the sniffer after supplying their settings and cache implementation. The
existing event types under `Events/` and data models under `Sniffing/` and
`RSHelpers/` are the supported integration surface today.

More complete API examples and a packaged distribution are planned as the
multiplayer API stabilizes.

## Contributing

Bug reports, reproducible Rocksmith state observations, documentation, and
focused pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) before
submitting captured data or code changes.

RockList develops reusable RockSnifferLib changes in this public repository
first, then syncs released revisions into RockList Desktop. The maintenance and
upstream process is documented in [MAINTAINING.md](MAINTAINING.md).

## License

MIT. See [LICENSE](LICENSE).
