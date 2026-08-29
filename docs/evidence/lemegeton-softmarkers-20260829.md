# Local soft-marker icon reference — 2026-08-29

## Provenance

The owner supplied Lemegeton 1.0.8.6 under the local `Lemegenton` folder. The
matching public source tag is
<https://github.com/paissaheavyindustries/Lemegeton/tree/v1.0.8.6> at commit
`c5faec95c0a8e8726a175b5e7dd6bc070425fa87`. Lemegeton is MIT licensed and its
license is retained in `THIRD_PARTY_NOTICES.md`.

`Lemegeton/Core/UserInterface.cs` maps the game icons used by soft markers:

| VedaMarker marker | Game icon ID |
| --- | ---: |
| Attack 1-4 | 61201-61204 |
| Bind 1-2 | 61211-61212 |
| Ignore 1-2 | 61221-61222 |

`Lemegeton/Plugin.cs` renders those icons locally by resolving the actor, adding
a world-space vertical offset, projecting the position with `WorldToScreen`, and
drawing the icon through ImGui. It stores soft-marker assignments locally rather
than writing the game's party marking controller.

## VedaMarker interpretation

VedaMarker uses the same documented game icon IDs and the same general local
world-to-screen presentation model. It does not copy Lemegeton's automation,
scheduler, configuration UI, or encounter modules. A complete Forsaken assignment
is still validated before the selected actor-to-icon mapping is replaced.

## Acceptance boundary

This source establishes reproducible icon identifiers and a local-only rendering
reference. It does not prove size, height, occlusion, or readability on the current
China client. Version 0.3.0 therefore includes a manual eight-icon preview; the
visual PoC remains incomplete until the owner confirms all eight icons appear above
the local player and disappear when cleared.
