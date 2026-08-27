# Real marker failure report — 2026-08-27

## Observed result

The owner reported that VedaMarker 0.2.2 produced no visible marker when the
target scope was set to self. This is failure evidence; real marking remains an
unchecked in-game PoC.

## Confirmed implementation defect

The command planner emitted `stop1` and `stop2` for the two ignore/prohibited
markers. The game `TextCommandParam` rows used by marking commands are
`ignore1` and `ignore2` (rows 102 and 104). Version 0.2.3 corrects those tokens
and resolves all eight marker parameters from the current client's localized
sheet before submission.

## Additional diagnostic controls

Version 0.2.3 adds a manually clicked self Attack 1 test, a self-clear test, the
last localized command submitted, and a readback of the current local marker
from `MarkingController`. It also waits at least 650 ms between the clear phase
and the new-marker phase.

Version 0.2.4 replaces the single-marker check with a manually started sequence
covering Attack 1-4, Bind 1-2, and Ignore 1-2. Each marker must be observed in
`MarkingController`, then the matching clear must be observed before the next
marker begins. The UI records a separate mark/clear result for all eight types.

The PoC may be checked only after the owner confirms that the current diagnostic
reports all eight mark/clear pairs as successful in the game client.

## 0.2.4 clear failure report

The owner subsequently reported that clearing appeared ineffective. The
diagnostic was waiting at least 650 ms from clear to the next marker, but only
the configured 150 ms from a marker to its clear command. Version 0.2.5 applies
the 650 ms minimum to both phase transitions while retaining the shorter
same-phase queue interval. This is a hypothesis-driven correction and does not
close the PoC until the owner reruns the in-game diagnostic successfully.
