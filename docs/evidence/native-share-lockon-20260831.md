# Native share LockOn — 2026-08-31

## Owner-supplied resource evidence

The owner-supplied Triggernometry export `3.xml` has SHA-256:

```text
ADF52D993DCF1A009BCFB263E2BFEC172636039C4B82EE190E8AC49E2F23E495
```

Its `[F] 分散/分摊 LockOn` trigger attaches `com_share3t` to the selected actor
when the normalized mechanic is share. The raw export is not committed because
it contains unrelated automation outside VedaMarker's product boundary.
VedaMarker does not load, execute, or depend on ACT, Triggernometry, or
PostNamazu.

The complete game resource path used by VedaMarker is:

```text
vfx/lockon/eff/com_share3t.avfx
```

## Native lifecycle reference

The actor-VFX creation and cleanup lifecycle was checked against the already
attributed MIT-licensed VFXEditor commit
`05f6eb0cce19a6a91ae275bce609299d573914b3`:

<https://github.com/0ceal0t/Dalamud-VFXEditor/commit/05f6eb0cce19a6a91ae275bce609299d573914b3>

`Interop/Structs/Vfx/ActorVfx.cs`, `Interop/ResourceLoader.Vfx.cs`, and
`Interop/Constants.cs` distinguish actor-attached VFX creation and removal from
the static Omen lifecycle. Version 0.3.3 uses that actor path locally, tracks only
its own returned handles, and clears them without writing party markers or game
combat state.

## Routing and cleanup

The state machine snapshot remains the source of truth for whether a role's
current mechanic is `Share`. `ForsakenShareTargetResolver` intersects those roles
with the user's configured marker target scope. The adapter clears the prior
LockOn before each replacement and also clears on manual test cleanup, mechanism
or wave transition, wipe, recommence, manual stop, territory change, duty
completion, error, and unload.

## Acceptance boundary

The resource evidence, state routing tests, and API 15 build do not prove that
the current China client renders `com_share3t`, that it follows the actor at the
expected height, or that every cleanup path is visually complete. The encounter
checkbox remains off by default until the owner confirms the manual display and
clear buttons in game.
