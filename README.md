# VedaMarker

VedaMarker is a standalone Dalamud plugin for the DMU P2 **Forsaken** encounter.
The current testing milestone provides deterministic role inference, automatic
eight-wave status recognition, dry-run marker assignments, an opt-in experimental
Party Marker queue, and a privacy-preserving evidence recorder.

## Current status

- P0 scaffold, manual controller, and redacted capture workflow: implemented and captured in game
- P1 role inference, Forsaken state machine, automatic wave recognition, and marker rules: implemented and unit tested
- Real party target markers: opt-in experimental provider; disabled by default pending an in-game submission/cleanup PoC
- Persistent native VFX: blocked on captured resource evidence
- Native AoE telegraphs: blocked on per-mechanic validation

The plugin never moves the player, simulates input, or executes combat actions.
Real party markers require manual role confirmation, an explicit experimental
toggle, and manual controller arming. Only one user-selected controller should
submit them.

## Install with Dalamud

Add the following URL under Dalamud Settings -> Experimental -> Custom Plugin
Repositories, then search for `VedaMarker` in the plugin installer:

```text
https://raw.githubusercontent.com/Coptone/VedaMarker/main/pluginmaster.json
```

The published package is a testing build. Experimental real party markers are
disabled by default; persistent native VFX and native AoE telegraphs remain
unavailable until their separate in-game evidence gates pass.

## Repository layout

```text
src/VedaMarker.Core          Game-independent rules and state
src/VedaMarker               Dalamud adapter, UI, and recorder
tests                         Core-rule and capture-privacy tests
docs                         Architecture, progress, and capture instructions
```

## Build

Core logic only:

```powershell
dotnet test tests/VedaMarker.Core.Tests/VedaMarker.Core.Tests.csproj -c Release
```

Full plugin builds also require a matching official Dalamud development
distribution and `DALAMUD_HOME`.

No open-source license is granted for this repository.
