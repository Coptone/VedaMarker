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

The PoC may be checked only after the owner confirms that the self-test visibly
marks Attack 1 and the readback changes to `攻击1` in the game client.
