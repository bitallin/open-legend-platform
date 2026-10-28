# Legend Unity SDK (2022 LTS)

100% original Legend protocol client for **Unity 2022 LTS** (PC + Android). No 996 / third-party engine protocol.

Protocol version: **1.7.0**

## Import

1. Create / open a Unity **2022.3 LTS** project (3D or URP).
2. Copy `client/unity-sdk/Runtime/` into `Assets/LegendSdk/Runtime/`.
3. Install NuGetForUnity (or manually reference) **Google.Protobuf** 3.28.x matching the server.
4. Ensure scripting define / API compatibility level supports C# 9+ (Unity 2022 default is fine).

## Scene setup

1. Create an empty GameObject `LegendDemo`.
2. Add component `LegendWorldDemoBehaviour`.
3. Set `Host` / `Port` to your `Legend.Host` (default `127.0.0.1:7100`).
4. Optional: fill `DebugBlockedCells` / `DebugSafeZones` from map JSON so Scene view Gizmos show walls (red) and safe zones (green).
5. Enter Play Mode → IMGUI panel: Login → Create/Select character → WASD / left-side virtual stick to move.
6. Combat (M2): leave safe zone, `RequestAttackAsync` / `RequestPickupAsync`.
7. Shop/Quest (M3): near NPC `npc_village_merchant`, `RequestTalkAsync` → `RequestShopBuyAsync` / `RequestQuestAcceptAsync` / `RequestQuestTurnInAsync`.
8. Equipment/Enhance (M4): `RequestEquipAsync(itemId, slot)` / `RequestUnequipAsync(slot)` / `RequestEnhanceEquipAsync` / `RequestUseItemAsync(slot)`.
9. Guild/Chat (M6): `RequestGuildCreateAsync` / `RequestGuildJoinAsync` / `RequestGuildLeaveAsync` / `RequestGuildInfoAsync` / `RequestChatAsync(channel, content)`.
10. Stall/Trade (M7): `RequestStallListAsync(itemId, count, unitPriceGold)` / `RequestStallBrowseAsync(itemIdFilter)` / `RequestStallBuyAsync(listingId)` / `RequestStallCancelAsync(listingId)` + `OnStallSoldNotify`.

## API flow

```
Connect → Handshake(1.7.0) → Register → Login
  → CharList / CreateChar / DeleteChar / SelectChar
  → EnterWorld  // Gold + Inventory + Quests + Equipment + guildId/Name/Rank + nearby NPCs/monsters
  → MoveReq / Attack / Pickup
  → TalkReq / ShopBuyReq / QuestAcceptReq / QuestTurnInReq
  → EquipReq / UnequipReq / EnhanceEquipReq / UseItemReq
  → GuildCreate / GuildJoin / GuildLeave / GuildInfo / ChatReq
  → StallList / StallBrowse / StallBuy / StallCancel (M7)
```

Helpers: `RequestTalkAsync`, `RequestShopBuyAsync`, `RequestQuestAcceptAsync`, `RequestQuestTurnInAsync`, `RequestEquipAsync`, `RequestUnequipAsync`, `RequestEnhanceEquipAsync`, `RequestUseItemAsync`, `RequestGuildCreateAsync`, `RequestGuildJoinAsync`, `RequestGuildLeaveAsync`, `RequestGuildInfoAsync`, `RequestChatAsync`, `RequestStallListAsync`, `RequestStallBrowseAsync`, `RequestStallBuyAsync`, `RequestStallCancelAsync`.
Properties: `Gold`, `EffectiveAtk`, `EffectiveDef`, `EffectiveMaxHp`, `Equipment`, `GuildId`, `GuildName`, `GuildRank`. Events: `OnQuestProgress`, `OnEquipmentChanged`, `OnChatNotify`, `OnStallSoldNotify`.

## Regenerating protocol stubs

```bash
dotnet build server/Legend.Contracts/Legend.Contracts.csproj -c Release
cp server/Legend.Contracts/obj/Release/net8.0/LegendProtocol.cs client/unity-sdk/Runtime/LegendProtocol.cs
```

Keep `Runtime/ProtocolConstants.cs` in sync with server (MsgId 2301–2306, 2401–2404, 2501–2510, 2601–2609, version 1.7.0).

## Notes

- Network callbacks via `PumpMainThreadEvents()` in `Update()`.
- Safe zone: all attacks banned; stall listing requires safe zone when `requireSafeZone: true`.
