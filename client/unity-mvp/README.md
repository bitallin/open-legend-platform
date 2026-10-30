# Legend Unity Client (Phase G · uGUI)

Protocol **1.21.0** · Target **Unity 2022.3 LTS** (PC first, touch stick compatible)

Playable client shell talking to local `Legend.Host`, with **runtime-built uGUI** (no IMGUI HUD):

1. Login / Register (Host/Port editable)
2. Character list / create / select / enter world
3. Grid entities + **select ring** highlight + nameplates
4. WASD + **uGUI virtual stick** move with MoveSync lerp
5. Click select + Attack / Pickup buttons + damage popups
6. HUD: HP bar · Lv · Gold · safe-zone badge · bag · chat · death / reconnect toasts

**Not in scope:** art polish, skill tree UI, payment mall UI, hot-update CDN. Launcher = Phase H/I; Android pack skeleton = Phase J.

## Open in Unity

```text
1. Install Unity Hub + Editor 2022.3.x LTS.
2. Hub → Open → select this folder:  client/unity-mvp/
3. From repo root (or this folder):  ./sync-sdk.sh
4. Wait for script compile. Google.Protobuf.dll is under Assets/Plugins/.
5. Hierarchy → Create Empty → Add Component → LegendClientBootstrap
   OR menu: Legend → Create MVP Boot Scene
6. Start Legend.Host (default 127.0.0.1:7100), then press Play.
```

### Host quick start

```bash
# from repo root
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release -- \
  --realm-config realms/realm-classic-01.json --realm-catalog realms
```

Default login: user `unity_mvp` / pass `pass123` (auto-register on first connect).

## Layout

```text
client/unity-mvp/
  sync-sdk.sh
  Packages/manifest.json          # includes com.unity.ugui
  Assets/
    Plugins/Google.Protobuf.dll
    LegendSdk/Runtime/            # synced from client/unity-sdk
    LegendClient/
      Scripts/
        Bootstrap/LegendClientBootstrap.cs
        UI/LegendHudController.cs     # uGUI shell
        UI/LegendUiBuilder.cs
        UI/LegendVirtualJoystickUi.cs
        UI/LegendToastOverlay.cs
        Session/… World/… Combat/… Input/… Config/…
        ClientCore/                   # synced pure FSM + ClientUiMode
      Editor/LegendMvpSceneBuilder.cs
```

Pure C# (unit-tested): `client/Legend.ClientCore/` (`ClientUiMode`, FSM, inventory, chat).

## Controls

| Action | PC | Touch |
|--------|----|-------|
| Move | WASD / arrows | Bottom-left uGUI stick |
| Select target | LMB on entity | Tap entity |
| Attack | Space / J / RMB / Attack button | Attack button |
| Pickup | Pickup button / click loot | Pickup / tap loot |
| Chat | World / Nearby + Send | same |

Colors: **self green · other players blue · monsters red · NPC yellow · loot gold · pet purple · selected yellow ring**.

## Manual acceptance checklist

- [ ] Unity opens `unity-mvp` without missing-script errors after `./sync-sdk.sh`
- [ ] Play → **uGUI** login panel (not IMGUI boxes) → character select → Enter world
- [ ] WASD / stick moves; select monster shows **yellow ring** + target HUD
- [ ] Leave safe zone (toast) → Attack → damage popup; kill toast
- [ ] Bag / chat work; Pickup button grabs nearby loot
- [ ] Kill Host → reconnect panel → Reconnect / Back to Login
- [ ] Self HP → 0 shows death toast

## Automated verify

```bash
./scripts/verify-phase-g-client.sh
# Phase F script still valid for SDK/FSM baseline:
./scripts/verify-phase-f-client.sh
```


## Android export (Phase J)

Pipeline skeleton for APK / AAB (IL2CPP · ARM64 · `com.legendplatform.mvp`).

```bash
# no Unity required — structure check:
./scripts/verify-phase-j-android.sh

# machine with Unity Android Module:
./scripts/build-android.sh          # → dist/android/*.apk
./scripts/build-android.sh --aab
```

Details: [`docs/ANDROID_BUILD.md`](../../docs/ANDROID_BUILD.md) · helpers under `AndroidBuild/`.

## Known limits

- This agent environment has **no Unity Editor** — Play Mode was not executed here.
- Runtime uGUI (Arial + white sprites); no art atlas / animations / SFX.
- Payment APIs exist on SDK but no mall UI.

## Playable PC (Phase K)

See [`docs/PLAYABLE_PC.md`](../../docs/PLAYABLE_PC.md) for Windows player build + launcher wiring.
