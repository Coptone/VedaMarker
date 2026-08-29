# Architecture

## Safety boundary

The combat path is local and deterministic. It consumes observed game events,
normalizes them, advances the Forsaken state machine, validates a complete set of
eight assignments, resolves the local user's role, and then calls an
`IMarkerProvider`. The local soft-marker provider defaults to the local actor and
may be explicitly configured for selected roles or all eight party members. It
does not mutate party state; persistent VFX and telegraph providers remain
separate PoCs.

```text
Dalamud observations
  -> redacted capture / normalized encounter events
  -> ForsakenStateMachine
  -> MarkerAssignmentResolver
  -> CompleteAssignmentValidator
  -> DryRunMarkerProvider
  -> LocalMarkerProvider (manually armed, configurable target scope, client-local)
  -> NativeOmenRenderer (explicit opt-in, tower MapEffect direction, client-local)
```

The cross-duty simulator bypasses encounter observations but not assignment or
safety validation. `ForsakenSimulationAssignmentFactory` supplies a complete
synthetic odd/even mechanic snapshot to the production `MarkerAssignmentResolver`;
the resulting eight-marker assignment then follows the same target selection,
replace-previous-mapping provider, and cleanup path as a real wave. The user
must arm it and submit every wave manually. Self-only simulation may omit the
party-slot map because its sole validated target is the selected local role. Any
target set containing another role still
requires the complete, unique eight-slot map and confirmed party roles.

For every new wave, the provider validates the complete eight-person assignment,
clears its previous local actor-to-icon mapping, then atomically installs the new
selected mapping. Completion, wipe, zone change, disarm, and unload clear the
mapping. Self-only remains the default.

The provider resolves actor IDs only after the complete assignment and target-scope
validator succeeds. During the UI draw callback it projects a point two world units
above each actor and draws the corresponding game icon there. Attack 1-4 use game
icons 61201-61204, Bind 1-2 use 61211-61212, and Ignore 1-2 use 61221-61222.
No marking function, chat command, or `MarkingController` write is used. A manual
preview walks through all eight icons on the local player for 1.5 seconds each and
clears between icons. The preview and encounter controller cannot run together.

The local AOE preview is a separate read-only presentation path. It anchors a
synthetic arena center at the local player's position, obtains odd/even range
geometry from the game-independent `ForsakenTelegraphPlanner`, and creates the
supplied game-native Omen resources. Wave and Direction8 are changed manually.
No party state is mutated.

The same native provider can be explicitly enabled for the encounter controller.
The opening Forsaken cast activates direction tracking. Two state-2 tower
`MapEffect` observations in the same recognized wave and within a two-second
window are converted from map-effect slots to Direction8 using the supplied ACT
reference. The current range is cleared as soon as a new assignment wave begins,
then recreated only after that wave's direction is complete. The arena center is
fixed at `(100, 100)` in X/Z and uses the local player's floor Y. Failure to
create or clear an Omen never stops or invalidates marker processing.

Role confirmation and controller authorization are scoped to the current duty
instance. A wipe clears marker/Omen state and resets the encounter state machine;
recommence automatically restores a previously authorized controller. Leaving
the territory, completing the duty, observing a real party-composition change,
encounter errors, or manual stop revokes authorization.

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
