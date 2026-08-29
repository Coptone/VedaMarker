# Progress

Last updated: 2026-08-29

## Confirmed decisions

- Standalone `Coptone/VedaMarker` repository; no VedaAxis integration.
- Custom Dalamud repository distribution; no official-repository target.
- China client current version first; identifiers rather than localized names.
- Manual controller arming for every pull; manual role confirmation persists across wipes/recommences in the current instance and resets on exit or party-composition change.
- Each armed plugin computes all eight assignments. Marker scope defaults to the local user and can be explicitly changed to selected roles or all eight players.
- Automatic role inference with configured tank/healer priorities and DPS role rules.
- Party markers are shared; native VFX and telegraphs are local-only.
- Native marker-controller path may be evaluated; no input simulation or chat-command fallback.
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
- [x] P1 marker command parameter correction, client-localized submission, and all-marker self-test UI
- [x] P1 manually armed cross-duty eight-wave marker simulator using the production assignment/provider path
- [x] P1 self-only single-player simulation with selectable local role and strict local-actor targeting
- [x] P1 experimental native marker provider with fail-closed signature discovery and controller-readback cleanup
- [x] P1 manually controlled local world-space station/range simulator for any duty
- [x] P0 schema-v2 whole-duty capture implementation and privacy/export test
- [ ] P0 in-game validation: schema-v2 action names/IDs, spatial observations, ActionEffect targets, and MapEffect events
- [ ] P0 in-game revalidation: all eight native Party Target Markers and clears through the 0.2.9 diagnostic/simulator
- [ ] P0 in-game solo O8S validation: manually submit eight simulated waves and confirm stop/cleanup
- [ ] P1 in-game visual validation: local odd/even station names, 30m/90-degree cones, 5m circles, and Direction8 rotation
- [ ] P1 in-game PoC: persistent point-name VFX
- [ ] P1 per-mechanic native telegraph validation

Items requiring game evidence remain unchecked even when the code builds.

## Verification

- Core rules, automatic capture replay, selected-target clear-before-mark ordering, solo simulation targeting, simulation assignments, and local telegraph geometry/rotation: 37 tests passed on .NET 10.
- Capture privacy/export: 1 test passed; schema-v2 ZIP contains only versioned manifest and JSONL.
- Dalamud API 15 Release build: 0 warnings, 0 errors against the official staging development files.
- Full in-game Forsaken status sequence is documented under `docs/evidence/`.
- Real marking remains experimental after the 0.2.2-0.2.6 chat-command path failed in game. Version 0.2.9 removes that runtime path and uses an evidence-backed native marker function plus `MarkingController` readback; missing signatures fail closed. The code/build checks do not close the in-game PoC.
- The 0.2.9 local AOE simulator implements supplied odd/even station geometry and verified 30m/90-degree cone and 5m circle parameters. It is manual and local-only; automatic DMU triggers, native Omen resources, and point-name VFX remain unavailable pending separate in-game validation.
