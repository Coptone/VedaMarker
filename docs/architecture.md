# Architecture

## Safety boundary

The combat path is local and deterministic. It consumes observed game events,
normalizes them, advances the Forsaken state machine, validates a complete set of
eight assignments, resolves the local user's role, and then calls an
`IMarkerProvider`. Dry-run remains the default. The experimental real-marker
provider defaults to `<me>` and may be explicitly configured for selected roles
or all eight party members; VFX and telegraph providers remain separate PoCs.

```text
Dalamud observations
  -> redacted capture / normalized encounter events
  -> ForsakenStateMachine
  -> MarkerAssignmentResolver
  -> CompleteAssignmentValidator
  -> DryRunMarkerProvider (default)
  -> ChatCommandMarkerProvider (experimental, manually armed, configurable target scope)
```

The cross-duty simulator bypasses encounter observations but not assignment or
safety validation. `ForsakenSimulationAssignmentFactory` supplies a complete
synthetic odd/even mechanic snapshot to the production `MarkerAssignmentResolver`;
the resulting eight-marker assignment then follows the same target selection,
clear-before-mark queue, provider, and cleanup path as a real wave. The user
must arm it and submit every wave manually. Self-only simulation may omit the
party-slot map because its sole validated target is the selected local role and
the command target is always `<me>`. Any target set containing another role still
requires the complete, unique eight-slot map and confirmed party roles.

For every new wave, the provider first queues clears for every selected target,
then queues new markers for every selected target from the validated eight-person
assignment. Completion, wipe, zone change, disarm, and unload clear the most
recently selected target set. Self-only remains the default.

Canonical commands are validated against a fixed whitelist. Marker parameters
are resolved through the current client's `TextCommandParam` rows before
submission, while cleanup is submitted unchanged as `/mk clear <target>`.
Any transition between a clear phase and a marker phase in
either direction is separated by at least 650 ms; commands inside one phase use
the configured queue interval. The provider records the last submitted
localized command. A manual diagnostic walks through all eight marker types on the local player,
reads each result from `MarkingController`, clears it, verifies the empty state,
and reports each mark/clear pair separately. The diagnostic and encounter
controller cannot run at the same time.

Role confirmation is scoped to the current instance. A wipe or duty recommence
disarms the controller and queues cleanup but retains confirmation; leaving the
territory or observing a real party-composition change resets it. Every pull
still requires the user to manually arm the controller.

## Role inference

- MT priority: WAR > PLD > GNB > DRK; the other tank becomes ST.
- H1 priority: WHM > AST > SGE > SCH; the other healer becomes H2.
- DPS reserves one physical-ranged player for D3 and one caster for D4.
- D1/D2 are then filled melee first, caster second, physical ranged last.
- Remaining non-standard compositions are filled deterministically by party-list order.
- A non-standard 2T/2H/4DPS party is never auto-confirmed.
- Users must review the inferred mapping before manually arming the controller.

## Evidence flow

The recorder is started and exported manually; it never uploads data. Schema v2
writes a versioned JSONL stream with session-local actor aliases. During the whole
duty it records territory/combat transitions, party jobs, status changes, cast
starts/ends, localized action names when available, action-sheet shape candidates,
source/target/ground positions, rotations, hitboxes, ActionEffect targets,
MapEffect events, and periodic party/relevant-world-object snapshots. It never
writes character names, Content IDs, account IDs, world names, or chat.

Action-sheet shape fields are candidates, not proof that every observed skill is
a native telegraph. Rendering remains evidence-gated per action because scripted
geometry, delayed effects, arena transforms, and client VFX can differ from the
static sheet row.
