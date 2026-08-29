# U7b P2 local telegraph reference — 2026-08-29

## Provenance and permission

The owner supplied a Triggernometry export named `3.xml` and explicitly approved
using it as a behavior reference. The raw export is not committed because it
contains unrelated automation and unsafe actions outside VedaMarker's product
boundary. VedaMarker does not load, execute, or depend on ACT or Triggernometry.

The inspected source SHA-256 is:

```text
ADF52D993DCF1A009BCFB263E2BFEC172636039C4B82EE190E8AC49E2F23E495
```

## Extracted geometry

Coordinates below are normalized around the source arena center `(100, 100)`;
VedaMarker stores them as relative `(X, Z)` offsets and rotates them in 45-degree
Direction8 steps.

Odd-wave station offsets:

| Label | X | Z |
|---|---:|---:|
| Share A | -6.30 | 4.70 |
| Idle A | -8.80 | 2.50 |
| Fan | -5.66 | 9.16 |
| Fan facing | -5.66 | 11.66 |
| Steel | 2.20 | 6.50 |
| Share B | 8.19 | 3.66 |
| Idle C | 8.70 | 2.34 |
| Idle D | 9.59 | 3.45 |

Even-wave station offsets:

| Label | X | Z |
|---|---:|---:|
| Fan A | -9.51 | 6.27 |
| Fan B | -8.30 | 8.52 |
| Steel A | 4.71 | 7.72 |
| Steel B | 8.69 | 3.22 |
| Idle A | -1.90 | -3.50 |
| Idle B | -3.20 | 2.40 |
| Idle C | 3.97 | -3.55 |
| Idle D | 3.20 | 2.40 |

The source configures fan ranges as 90-degree cones with scale/range 30 and
steel ranges as circles with scale/radius 5. VedaMarker draws one odd-wave cone
and circle, or two even-wave cones and circles, matching the extracted strategy
layout.

The supplied callbacks use the following native game resources:

| Purpose | Resource |
|---|---|
| 90-degree fan | `vfx/omen/eff/z6r3_b4_fan90_k2.avfx` |
| Steel circle | `vfx/omen/eff/m0347_sircle_01m1.avfx` |

The source reads tower MapEffect log rows whose state begins with `0002` and
whose slot is `01` through `08`. It converts each slot with `(9 - slot) % 8`,
interpolates the two tower directions, adds four, and reduces modulo eight to
produce the range `Direction8`. VedaMarker implements that conversion only for
two state-2 observations belonging to the same already-recognized wave and
arriving within two seconds; incomplete or ambiguous pairs fail closed.

## Acceptance boundary

Version 0.3.1 exposes a manually started native-Omen preview centered on the
local player's position and an explicit opt-in Forsaken runtime path centered at
the source arena `(100,100)`. The runtime clears the prior range at every wave
transition and waits for the new wave's complete tower-direction pair before
creating its range.

The export supports implementation but does not replace in-game validation of
native resource scale/orientation, live MapEffect ordering, cleanup, point-name
VFX resources, or every encounter AOE. The encounter provider therefore remains
off by default until the owner confirms the visual PoC.
