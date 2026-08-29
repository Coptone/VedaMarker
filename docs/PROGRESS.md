# Progress

Last updated: 2026-08-29

## Confirmed decisions

- Standalone `Coptone/VedaMarker` repository; no VedaAxis integration.
- Custom Dalamud repository distribution; no official-repository target.
- China client current version first; identifiers rather than localized names.
- Manual controller arming once per duty instance; after that explicit authorization, wipes/recommences clear local output and automatically restore the controller. Authorization and role confirmation reset on exit, completion, party-composition change, error, or manual stop.
- Each armed plugin computes all eight assignments. Marker scope defaults to the local user and can be explicitly changed to selected roles or all eight players.
- Automatic role inference with configured tank/healer priorities and DPS role rules.
- Attack/Bind/Ignore assignment icons are local soft markers; VedaMarker does not mutate shared Party Target Markers.
- The supplied ACT geometry and native Omen resource paths are integrated as an explicit opt-in local provider; each wave still requires in-game timing/orientation/cleanup validation before it can become the default.
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
- [x] P1 historical 0.2.9 native marker provider with fail-closed signature discovery and controller-readback cleanup
- [x] P1 replace shared party-marker runtime with local Attack/Bind/Ignore soft markers and configurable target scope
- [x] P1 manually controlled local world-space station/range simulator for any duty
- [x] P1 replace projected range polygons with a local native-Omen preview and connect the same provider to Forsaken wave/MapEffect recognition behind an explicit opt-in
- [x] P1 retain one explicit controller authorization across duty wipes and automatically restore it on recommence
- [x] P0 schema-v2 whole-duty capture implementation and privacy/export test
- [ ] P0 in-game validation: schema-v2 action names/IDs, spatial observations, ActionEffect targets, and MapEffect events
- [ ] P0 in-game visual validation: preview all eight local marker icons and confirm every icon clears
- [ ] P0 in-game solo O8S validation: manually submit eight local-marker waves and confirm stop/cleanup
- [ ] P1 in-game visual validation: local odd/even station names, 30m/90-degree cones, 5m circles, and Direction8 rotation
- [ ] P1 in-game PoC: persistent point-name VFX
- [ ] P1 per-mechanic native telegraph validation

Items requiring game evidence remain unchecked even when the code builds.

## Verification

- Core rules, automatic capture replay, selected-target clear-before-mark ordering, solo simulation targeting, simulation assignments, local telegraph geometry/rotation, and tower-direction pairing: 46 tests passed on .NET 10.
- Capture privacy/export: 1 test passed; schema-v2 ZIP contains only versioned manifest and JSONL.
- Dalamud API 15 Release build: 0 warnings, 0 errors against the official staging development files.
- Full in-game Forsaken status sequence is documented under `docs/evidence/`.
- The 0.3.0 runtime removes shared party-marker mutation. It uses game icon assets 61201-61204, 61211-61212, and 61221-61222 as local world-to-screen soft markers. The code/build checks do not close the in-game visual PoC.
- The 0.3.1 native AOE path uses the supplied `z6r3_b4_fan90_k2` cone and `m0347_sircle_01m1` circle resources. Manual cross-duty preview and opt-in Forsaken runtime wiring are implemented; code-level checks do not close the native visual, Direction8, timing, or cleanup PoCs.
- The 0.3.1 release ZIP SHA-256 is `1650BDD01CA660434F8BDF5040A02655F0208744EB2CCAA9AA12BDE6446607C9`.
