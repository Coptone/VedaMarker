# VedaMarker repository instructions

## Product boundary

VedaMarker is a standalone Dalamud plugin for DMU P2 Forsaken duty assignment,
party target markers, local persistent native VFX, and validated local native
telegraphs. It is intentionally separate from VedaAxis.

- Never move, press keys, simulate input, or execute combat actions.
- Real party markers may only run after the local user manually arms the controller. The default target is the local user; selected-role and full-party modes require an explicit local choice.
- Never submit a partial marker set when all eight responsibilities are unknown.
- Persistent VFX and telegraphs are local-only and must never block marker cleanup.
- Do not add ACT, Triggernometry, PostNamazu, or a network service dependency.
- Do not add an open-source license unless the owner explicitly changes this decision.

## Evidence rules

- Do not hard-code territory, action, status, VFX, or telegraph identifiers without
  a redacted capture or another reproducible source in `docs/evidence/`.
- Do not mark a technical validation item complete because code builds.
- Keep captures and character-identifying logs out of Git. The built-in recorder
  must export aliases rather than character names, account IDs, or world names.
- Native party marking, VFX creation, and telegraph creation each require an
  in-game PoC before their provider can become the default.

## Architecture

- `src/VedaMarker.Core` contains game-independent role inference, state-machine,
  validation, and marker-assignment logic.
- `src/VedaMarker` is the Dalamud adapter, UI, capture recorder, and future native providers.
- `tests/VedaMarker.Core.Tests` covers every confirmed business rule.
- Keep game hooks and unsafe code outside `VedaMarker.Core`.

## Required checks

```powershell
dotnet test tests/VedaMarker.Core.Tests/VedaMarker.Core.Tests.csproj -c Release
dotnet build src/VedaMarker/VedaMarker.csproj -c Release
git diff --check
```

The plugin build requires .NET 10 and a matching official Dalamud development
distribution through `DALAMUD_HOME`. Core tests do not require Dalamud.
