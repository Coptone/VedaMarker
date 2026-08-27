# Progress

Last updated: 2026-08-27

## Confirmed decisions

- Standalone `Coptone/VedaMarker` repository; no VedaAxis integration.
- Custom Dalamud repository distribution; no official-repository target.
- China client current version first; identifiers rather than localized names.
- Manual controller arming and manual role confirmation.
- Each armed plugin computes all eight assignments but clears and marks only its local user on every wave.
- Automatic role inference with configured tank/healer priorities and DPS role rules.
- Party markers are shared; native VFX and telegraphs are local-only.
- Native controller or programmatic `/mk` path may be evaluated; no input simulation.
- No open-source license.

## Milestones

- [x] P0 repository/build scaffold
- [x] P0 redacted evidence recorder and export instructions
- [x] P0 dry-run controller UI and role confirmation
- [x] P1 automatic role inference with tests
- [x] P1 Forsaken eight-wave state machine with tests
- [x] P1 odd/even marker assignment rules with tests
- [x] P0 in-game capture: encounter/phase/wave evidence
- [x] P1 automatic status-to-wave recognition and complete self-only marker queue
- [ ] P0 in-game PoC: real Party Target Marker and throttling
- [ ] P1 in-game PoC: persistent point-name VFX
- [ ] P1 per-mechanic native telegraph validation

Items requiring game evidence remain unchecked even when the code builds.

## Verification

- Core rules, automatic capture replay, and self-only command ordering: 18 tests passed on .NET 10.
- Capture privacy/export: 1 test passed; ZIP contains only versioned manifest and JSONL.
- Dalamud API 15 Release build: 0 warnings, 0 errors against the official staging development files.
- Full in-game Forsaken status sequence is documented under `docs/evidence/`.
- Real marking, VFX, and telegraph PoCs remain incomplete; self-only real markers are experimental and disabled by default, while VFX/AoE remain unavailable.
