# DMU P2 data capture

## What to send

Use the plugin's **采集与诊断** panel:

1. Enter DMU and open `/vedamarker` before the duty starts (or before entering combat).
2. Click **开始脱敏采集**.
3. Keep capture running for the entire duty, including every phase and transition.
4. Complete the duty when possible; otherwise keep any pull that reaches a new mechanic.
5. Click **停止并导出 ZIP**.
6. Attach the generated `VedaMarker-capture-*.zip` to the Codex task.

Schema v2 records every observed cast and ActionEffect action ID, a localized
action name when the Action sheet provides one, cast timing, source/target/ground
coordinates, rotations, hitboxes, ActionEffect targets, MapEffect events, and a
position snapshot every 0.5 seconds. It also keeps the existing party, status,
territory, combat, wipe, recommence, and completion evidence.

Also provide screenshots or a recording for these moments when possible:

- all eight opening point-name visuals;
- one odd-wave expected marker result;
- one even wave showing "near swaps, far stays";
- the new point names after Wave 3 and their Wave 8 resolution;
- any desired native AoE shape, size, position, and facing.

## Video

MP4 is usable: Codex can extract frames around log timestamps and inspect the
mechanic visually. To keep files practical, prefer H.264 or H.265 at 1080p/30;
720p/30 is also enough when the telegraph edges remain readable. If the recording
is large, split it into roughly 10-minute parts. Start capture and recording close
together and keep the game clock or a visible countdown in frame when possible;
this makes JSONL-to-video alignment much easier.

## Privacy

The ZIP uses `P1` through `P8` and `N1`, `N2`, ... session-local aliases.
It excludes character names, account/Content IDs, world names, chat, and network
credentials. Review the JSONL before sharing if desired.

## Evidence levels

The expanded capture identifies candidate territory, cast, ActionEffect, status,
MapEffect, and spatial evidence across the full duty. Static Action-sheet range
fields are only candidates: they cannot by themselves prove all native AoE shapes.
Skills driven by scripted geometry, arena transforms, delayed positions, or
client-only VFX still need log/video correlation and a per-mechanic validation.
If the point names are client-only VFX with no usable status or action event, a
second targeted recorder will be added for VFX creation paths.
