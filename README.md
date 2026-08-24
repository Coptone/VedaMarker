# VedaMarker

VedaMarker is a standalone Dalamud plugin for the DMU P2 **Forsaken** encounter.
The first milestone provides deterministic role inference, an eight-wave state
machine, dry-run marker assignments, and a privacy-preserving evidence recorder.

## Current status

- P0 scaffold, manual dry-run controller, and redacted capture workflow: implemented; awaiting in-game capture
- P1 role inference, Forsaken state machine, and marker rules: implemented and unit tested
- Real party target markers: blocked on an in-game PoC
- Persistent native VFX: blocked on captured resource evidence
- Native AoE telegraphs: blocked on per-mechanic validation

The plugin never moves the player, simulates input, or executes combat actions.
Only one user-selected controller may eventually submit real party markers.

## Install with Dalamud

Add the following URL under Dalamud Settings -> Experimental -> Custom Plugin
Repositories, then search for `VedaMarker` in the plugin installer:

```text
https://raw.githubusercontent.com/Coptone/VedaMarker/main/pluginmaster.json
```

The currently published package is the capture/testing build. Real party
markers, persistent native VFX, and native AoE telegraphs remain disabled until
their in-game evidence gates pass.

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
