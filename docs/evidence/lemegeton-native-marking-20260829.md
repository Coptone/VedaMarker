# Native party-marker reference — 2026-08-29

> Historical evidence: version 0.3.0 removed the native Party Marker provider
> from the runtime. This document is retained only to explain the superseded
> 0.2.9 implementation and must not be treated as the current marker path.

## Provenance

The owner supplied the installed Dalamud plugin files under the local folder
`Lemegenton`. Its manifest identifies Lemegeton 1.0.8.6 and links the public
repository at <https://github.com/paissaheavyindustries/Lemegeton>.

The matching source tag was inspected at
<https://github.com/paissaheavyindustries/Lemegeton/tree/v1.0.8.6> (commit
`c5faec95c0a8e8726a175b5e7dd6bc070425fa87`). The relevant implementation is
in `Lemegeton/Core/State.cs` and `Lemegeton/Core/AutomarkerSigns.cs`. Lemegeton
is distributed under the MIT License; attribution is retained in the repository
third-party notice.

The supplied DLL SHA-256 is:

```text
D9560B776B1A132B7316C8944789E9A7C4399C8F3013D5E0E1C66803EBFCFFEE
```

## Reproduced native contract

The tagged source resolves a function with this signature pattern:

```text
48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC 20 8D 42
```

It invokes the function as `(markingController, markerIndex, actorId)`. The
marker indices needed by VedaMarker are Attack 1-4 = 1-4, Bind 1-2 = 6-7, and
Ignore 1-2 = 9-10. To remove a marker, the reference implementation first
finds the actor's current marker and invokes the same marker index again.

VedaMarker 0.2.9 follows that contract but obtains the controller pointer from
the API-15 `FFXIVClientStructs.MarkingController.Instance()` accessor already
used for readback. It does not copy Lemegeton's scheduler or command fallback.
If signature discovery or controller access fails, the provider refuses to run.

## Acceptance boundary

This source establishes a reproducible implementation reference, not proof that
the current China client accepts the call. Real marking remains disabled by
default. The native provider may pass the PoC only after the owner runs the
all-eight self diagnostic and observes every mark and clear through controller
readback in game.
