# Architecture

## Safety boundary

The combat path is local and deterministic. It consumes observed game events,
normalizes them, advances the Forsaken state machine, validates a complete set of
eight assignments, and then calls an `IMarkerProvider`. The initial provider is
dry-run only. Native marker, VFX, and telegraph providers remain separate PoCs.

```text
Dalamud observations
  -> redacted capture / normalized encounter events
  -> ForsakenStateMachine
  -> MarkerAssignmentResolver
  -> CompleteAssignmentValidator
  -> DryRunMarkerProvider (P0/P1)
  -> NativePartyMarkerProvider (future, manually armed)
```

## Role inference

- MT priority: WAR > PLD > GNB > DRK; the other tank becomes ST.
- H1 priority: WHM > AST > SGE > SCH; the other healer becomes H2.
- DPS reserves one physical-ranged player for D3 and one caster for D4.
- D1/D2 are then filled melee first, caster second, physical ranged last.
- Remaining non-standard compositions are filled deterministically by party-list order.
- A non-standard 2T/2H/4DPS party is never auto-confirmed.
- Users must review the inferred mapping before manually arming the controller.

## Evidence flow

The recorder writes a versioned JSONL stream with session-local actor aliases.
It records territory/combat transitions, party jobs, status changes, cast starts
and ends, and observed ActionEffect IDs. It never writes character names,
Content IDs, account IDs, or world names.
