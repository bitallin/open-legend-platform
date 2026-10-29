# Unity Client uGUI (Phase G)

Protocol **1.21.0** (unchanged). Replaces Phase F IMGUI HUD with a runtime-built **uGUI** shell.

## What you get

| Panel | Contents |
|-------|----------|
| Login | Host / Port / User / Pass + Connect |
| Char select | List → Enter, create name/class |
| HUD | HP bar, Lv/Gold/Map, target, safe-zone badge |
| Bag | Toggle, item list + gold |
| Chat | World / Nearby + input |
| Controls | Bottom-left virtual stick, Attack, Pickup |
| Overlays | Damage popups, select ring, toast (safe zone / death / kill), reconnect |

## Open & play

Same as Phase F — empty scene + `LegendClientBootstrap`:

```text
1. Unity Hub → Open → client/unity-mvp/
2. ./sync-sdk.sh
3. Create Empty → Add Component → LegendClientBootstrap
   OR Legend → Create MVP Boot Scene
4. Start Legend.Host on 127.0.0.1:7100 → Play
```

Default account: `unity_mvp` / `pass123`.

## Verify

```bash
./scripts/verify-phase-g-client.sh
```

## Layout (new / changed)

```text
client/Legend.ClientCore/ClientUiMode.cs          # panel mode ↔ SessionPhase
client/unity-mvp/Assets/LegendClient/Scripts/UI/
  LegendUiBuilder.cs                             # runtime Canvas/Button/Input helpers
  LegendVirtualJoystickUi.cs                     # uGUI stick
  LegendToastOverlay.cs                          # center toasts
  LegendHudController.cs                         # Phase G shell (was IMGUI)
World/LegendEntityView.cs                        # yellow select ring
```

## Known limits

- No Unity Editor in this agent environment — Play Mode not executed here.
- Runtime uGUI uses built-in Arial / white sprites (no art atlas).
- Still no launcher / hot-update / mall / polished animations.

## Android packing

See Phase J: [`ANDROID_BUILD.md`](ANDROID_BUILD.md) and `./scripts/verify-phase-j-android.sh`.
