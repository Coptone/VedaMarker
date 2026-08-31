# Native Omen run entry and soft-marker preload — 2026-08-31

## Provenance

The native static-VFX lifecycle was checked against VFXEditor commit
`05f6eb0cce19a6a91ae275bce609299d573914b3`, an MIT-licensed Dalamud plugin
already attributed in `THIRD_PARTY_NOTICES.md`:

<https://github.com/0ceal0t/Dalamud-VFXEditor/commit/05f6eb0cce19a6a91ae275bce609299d573914b3>

`VFXEditor/Interop/Structs/Vfx/StaticVfx.cs` uses separate operations to create
and run a static VFX. `VFXEditor/Interop/Constants.cs` identifies the static run
entry with:

```text
E8 ?? ?? ?? ?? B0 02 EB 02
```

Version 0.3.1 created a `VfxObject` but incorrectly treated
`VfxObject.Addresses.Update` as the static run function. A non-null object was
therefore reported as a successful Omen even though the resource had not followed
the referenced create/run lifecycle. Version 0.3.2 scans the distinct run entry,
calls it after creation, then applies the position, scale, rotation, and transform
update.

The soft-marker behavior was checked against owner-supplied Lemegeton 1.0.8.6
and matching public commit `c5faec95c0a8e8726a175b5e7dd6bc070425fa87`, also
MIT-licensed and already attributed. `Lemegeton/Core/UserInterface.cs` requests
all marker textures during `LoadTextures`, including Attack, Bind, and Ignore,
rather than waiting for the first transition to each category.

<https://github.com/paissaheavyindustries/Lemegeton/tree/c5faec95c0a8e8726a175b5e7dd6bc070425fa87>

Version 0.3.2 likewise requests all eight Forsaken marker icons during provider
construction. If a requested texture is still unavailable when a frame is drawn,
the actor-to-marker assignment remains active and the next frame retries it; the
control panel exposes the current draw status.

## Acceptance boundary

The corrected function routing and preload behavior can be reviewed and compiled
without the game client. They do not prove that the China client renders the two
supplied Omen resources with the expected scale and orientation, nor that every
Attack/Bind/Ignore transition is visually stable. Both remain explicit in-game
validation items.
