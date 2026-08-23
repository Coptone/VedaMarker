# DMU P2 data capture

## What to send

Use the plugin's **采集与诊断** panel:

1. Enter DMU and open `/vedamarker` before the P2 transition.
2. Click **开始脱敏采集**.
3. Record at least one pull from the P2 transition until a wipe or clear.
4. Prefer two additional pulls that reach Wave 4 and Wave 8.
5. Click **停止并导出 ZIP**.
6. Attach the generated `VedaMarker-capture-*.zip` to the Codex task.

Also provide screenshots or a short recording for these moments when possible:

- all eight opening point-name visuals;
- one odd-wave expected marker result;
- one even wave showing "near swaps, far stays";
- the new point names after Wave 3 and their Wave 8 resolution;
- any desired native AoE shape, size, position, and facing.

## Privacy

The ZIP uses `P1` through `P8` and `N1`, `N2`, ... session-local aliases.
It excludes character names, account/Content IDs, world names, chat, and network
credentials. Review the JSONL before sharing if desired.

## Evidence levels

The first capture is expected to identify candidate territory, cast, ActionEffect,
and status IDs. If the point names are client-only VFX with no usable status or
action event, a second targeted recorder will be added for VFX creation paths.
