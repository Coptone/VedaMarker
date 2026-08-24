# Forsaken status sequence evidence (2026-08-24)

## Scope and privacy

- Client: China-region client; the exact game build was not recorded by capture schema v1.
- Territory: `1363`.
- Encounter phase: DMU P2 Forsaken through completion and entry into P3.
- Full session alias: `c9ca22a8905d47e09044993428a032d1`.
- Full capture: 3,658 contiguous events, 178.888 seconds, manually exported in P3.
- Comparison capture: a separate wipe at the sixth transition, 1,888 contiguous events.
- Review date: 2026-08-24.
- Reviewers: repository owner supplied both captures; Codex derived this redacted summary.

The raw captures remain outside Git. Their party records use only session-local
aliases (`P1`...`P8`, `N*`) and contain no names, Content/account IDs, world
names, chat, credentials, or source/target entity-ID fields.

SHA-256 checksums:

| Capture | File | SHA-256 |
| --- | --- | --- |
| Full | `manifest.json` | `E47BD4B02A6536C5AD28446EF60A9852FB8534B2A5404ECB9EBB599E0AB63392` |
| Full | `events.jsonl` | `BE2D1FA1D85CE0B4029BDD7435EB0D259E773A9D54EB0A2BDA847994928C65E8` |
| Wipe | `manifest.json` | `5851516A7B2CE9EDC217A13500194435AFABC0A41927B696028D0942052A5F03` |
| Wipe | `events.jsonl` | `B594CBFEA02C7F80568258B842D779C58F54FF17B645F9BD7031CE0A8837C486` |

## Accepted identifiers

| Identifier | Interpretation | Evidence and confidence |
| --- | --- | --- |
| Territory `1363` | Encounter territory | Only territory observed in both sessions; high confidence for this client build. |
| Action `47804` | Forsaken | Cast from 21.653s to 28.658s immediately before the opening statuses; high confidence. |
| Action `47805` | Light of Judgment / end boundary | Cast from 129.824s to 134.852s after all Forsaken statuses cleared; high confidence. |
| Status `5083`, param `4..1` | Remaining inventory/count | All eight start at 4; each active group transitions 3, 2, 1 before clearing; high confidence. |
| Status `5084` | Share | Opening holders form the two documented tower pairs A and C; high confidence. |
| Status `5085` | Steel | Mutually exclusive mechanic status observed across all transition points; high confidence. |
| Status `5086` | Fan | Mutually exclusive mechanic status observed across all transition points; high confidence. |

The `5084`/`5085`/`5086` interpretation is also consistent with every role's
documented marker responsibility and with the full eight-wave sequence. The
plugin rejects a snapshot if a role has multiple mechanic statuses.

## Reproducible transition sequence

The full capture resolves roles as tower group `MT/H1/D1/D3` (pairs A/C) and
idle group `ST/H2/D2/D4` (pairs B/D).

| Elapsed | Observed status transition | State-machine interpretation |
| ---: | --- | --- |
| 30.185s | All eight: `5083 param=4` plus one of `5084..5086` | Opening complete; Wave 1 uses initial tower group |
| 42.433s | Tower group: `param=3` plus complete mechanics | Wave 2 |
| 52.494s | Tower group: `param=2` plus complete mechanics | Wave 3 |
| 63.542s | Tower group: `param=1` plus complete mechanics | Save Pending for Wave 8; Wave 4 uses idle group's opening mechanics |
| 73.501s | Idle group: `param=3` plus complete mechanics | Wave 5 |
| 84.548s | Idle group: `param=2` plus complete mechanics | Wave 6 |
| 94.501s | Idle group: `param=1` plus complete mechanics | Wave 7 |
| 105.548s | Idle group's `5083..5086` statuses clear | Wave 8 restores tower-group Pending mechanics |
| 115.496s | Tower group's `5083..5086` statuses clear | Forsaken complete; clear party markers |

The shorter wipe capture independently matches the same opening and transition
timing/order through the sixth observed transition, then ends on the wipe.

## Acceptance boundary

This evidence is sufficient to enable automatic encounter-status recognition,
the eight-wave marker-assignment engine, and an experimental manually armed
Party Marker command queue. It is not an in-game proof that the command queue's
rate or cleanup is accepted by the client; that PoC remains open and the real
marker provider therefore remains disabled by default.

Capture schema v1 does not include actor coordinates, cast target/rotation,
MapEffect payloads, or VFX resource paths. It cannot validate AoE position,
radius, direction, lifetime, or native telegraph resources. AoE and persistent
VFX providers must remain disabled until a follow-up capture/PoC supplies that
evidence.
