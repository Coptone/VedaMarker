# Progress

Last updated: 2026-08-27

## Confirmed decisions

- Standalone `Coptone/VedaMarker` repository; no VedaAxis integration.
- Custom Dalamud repository distribution; no official-repository target.
- China client current version first; identifiers rather than localized names.
- Manual controller arming and manual role confirmation.
- Each armed plugin computes all eight assignments. Marker scope defaults to the local user and can be explicitly changed to selected roles or all eight players.
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
- [x] P1 automatic status-to-wave recognition and complete configurable-target marker queue
- [x] P1 marker command parameter correction, client-localized submission, and self-test UI
- [x] P0 schema-v2 whole-duty capture implementation and privacy/export test
- [ ] P0 in-game validation: schema-v2 action names/IDs, spatial observations, ActionEffect targets, and MapEffect events
- [ ] P0 in-game revalidation: real Party Target Marker after 0.2.3 parameter/throttling fix
- [ ] P1 in-game PoC: persistent point-name VFX
- [ ] P1 per-mechanic native telegraph validation

Items requiring game evidence remain unchecked even when the code builds.

## Verification

- Core rules, automatic capture replay, selected-target clear-before-mark ordering, and ignore-parameter regression: 21 tests passed on .NET 10.
- Capture privacy/export: 1 test passed; schema-v2 ZIP contains only versioned manifest and JSONL.
- Dalamud API 15 Release build: 0 warnings, 0 errors against the official staging development files.
- Full in-game Forsaken status sequence is documented under `docs/evidence/`.
- Real marking remains experimental after the 0.2.2 in-game failure report. Version 0.2.3 corrects `stop1/stop2` to the game's `ignore1/ignore2`, localizes every parameter, adds a 650ms clear-to-mark phase delay, and adds a direct self-test; in-game revalidation is still required. VFX/AoE remain unavailable.
