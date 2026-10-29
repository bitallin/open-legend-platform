# Legend Unity Client MVP (Phase F)

Protocol **1.21.0** · Target **Unity 2022.3 LTS** (PC first, touch stick compatible)

Playable skeleton that talks to local `Legend.Host`:

1. Login / Register  
2. Character list / create / select / enter world  
3. Grid entities (self / players / monsters / NPCs / loot) as colored primitives + nameplates  
4. WASD + left virtual stick move with MoveSync lerp  
5. Click select + Attack (Space / RMB / on-screen button) with CombatSync damage popups  
6. HUD: HP / Level / Gold, bag, world/nearby chat, disconnect → reconnect  

**Not in scope:** art polish, skill tree UI, payment mall, launcher, iOS-only work, hot-update CDN.

## Open in Unity

```text
1. Install Unity Hub + Editor 2022.3.x LTS.
2. Hub → Open → select this folder:  client/unity-mvp/
3. From repo root (or this folder):  ./sync-sdk.sh
   (copies client/unity-sdk/Runtime + ClientCore FSM into Assets/)
4. Wait for script compile. Google.Protobuf.dll is under Assets/Plugins/.
5. Hierarchy → Create Empty → rename LegendBoot → Add Component → LegendClientBootstrap
   OR menu: Legend → Create MVP Boot Scene
6. Start Legend.Host (default 127.0.0.1:7100), then press Play.
```

### Host quick start

```bash
# from repo root
dotnet run --project server/Legend.Host/Legend.Host.csproj -c Release -- \
  --realm-config realms/realm-classic-01.json --realm-catalog realms
# (adjust ports / sqlite as in your realm JSON)
```

Default MVP login fields: user `unity_mvp` / pass `pass123` (auto-register on first connect).

## Layout

```text
client/unity-mvp/
  sync-sdk.sh
  Packages/manifest.json
  ProjectSettings/          # minimal 2022.3 project stub
  Assets/
    Plugins/Google.Protobuf.dll
    LegendSdk/Runtime/      # synced from client/unity-sdk
    LegendClient/
      Scripts/
        Bootstrap/LegendClientBootstrap.cs   # runtime scene build
        Session/LegendSessionService.cs
        World/…  Combat/…  UI/…  Input/…  Config/…
        ClientCore/         # synced pure FSM
      Editor/LegendMvpSceneBuilder.cs
```

Pure C# FSM (unit-tested without Unity): `client/Legend.ClientCore/`.

## Controls

| Action | PC | Touch |
|--------|----|-------|
| Move | WASD / arrows | Left-half virtual stick |
| Select target | LMB on entity | Tap entity |
| Attack | Space / J / RMB / ATTACK button | ATTACK button |
| Chat | World / Nearby + Send | same |
| Pickup loot | Click loot sphere | Tap loot |

Colors: **self green · other players blue · monsters red · NPC yellow · loot gold · pet purple**.

## Manual acceptance checklist

- [ ] Unity opens `unity-mvp` without missing-script errors after `./sync-sdk.sh`
- [ ] Play → Login → character select → Enter world → see green capsule + ground
- [ ] WASD moves; remote MoveSync entities lerp
- [ ] Leave safe zone, select red monster, Attack → damage popup + HP bar drops
- [ ] Bag shows inventory after loot/pickup; Gold on HUD updates
- [ ] Chat World/Nearby sends and shows in log
- [ ] Kill Host process → reconnect banner → Reconnect works with same account

## Automated verify

```bash
./scripts/verify-phase-f-client.sh
```

Checks protocol version alignment, required files, ClientCore unit tests, SDK sync, and a CliClient smoke against a temp Host.

## Known limits

- This agent environment has **no Unity Editor** — Play Mode was not executed here.
- IMGUI HUD (not uGUI canvases) for zero-asset bootstrap.
- Placeholder primitives only; no animation / SFX.
- Payment APIs exist on SDK but no mall UI.
