# 协议版本说明

当前对外版本字符串：**1.21.0**（`protocol/VERSION` 与 `ProtocolConstants.CurrentVersion`）。

Phase L 之前，可玩客户端只使用握手、登录、选角、进世界、移动、攻击、拾取、对话、使用物品、聊天。商店购买、任务、装备、技能列表、组队、行会、行会仓库等报文在服务端已经存在，客户端未接。

## 为何还不是 1.22.0

`scripts/verify-m19.sh`、`verify-phase-*.sh`、`verify-playable-client.sh` 把版本字符串固定成 `1.21.0`。在这些脚本一起改完之前，不改版本号。

若 Phase L 增加字段或 MsgId，记在下面，并保持旧客户端握手仍被接受（`MinCompatibleVersion` 维持 1.21.0）。正式升到 **1.22.0** 时要同时改：

- `protocol/VERSION`
- `server/Legend.Contracts/ProtocolConstants.cs`
- 上述验收脚本里的期望版本
- 可玩客户端 `SmokeCheck` / 测试里的字面量
- 本文件与 `docs/DEV_PROGRESS.md`

## 已有、Phase L 计划复用的报文（无需升版本）

| 范围 | MsgId |
|------|-------|
| 商店购买 / 任务 | 2201–2209 |
| 装备 / 强化 | 2301–2306 |
| 技能学习与列表 | 2801–2808 |
| 聊天 | 2509–2510 |
| 组队 | 2701–2710 |
| 行会 | 2501–2508、3701–3704 |
| 行会仓库 | 3601–3608 |

## Phase L 已追加、版本字符串未改

旧客户端不发这些报文即可。新服务端仍接受 1.21.0 握手。

| MsgId | 报文 |
|-------|------|
| 3901 / 3902 | `ShopSellReq` / `ShopSellResp` |
| 3903 / 3904 | `RepairReq` / `RepairResp` |
| 3905 / 3906 | `StorageListReq` / `StorageListResp` |
| 3907 / 3908 | `StorageDepositReq` / `StorageDepositResp` |
| 3909 / 3910 | `StorageWithdrawReq` / `StorageWithdrawResp` |
| 枚举 | `ChatChannel.CHAT_PARTY = 4` |

个人仓库堆叠写在角色 `appearance_json.storage`，没有新的数据库列。修理按已装备强化等级扣金币，**还没有耐久度**。

## 仍缺、升到 1.22.0 时再做的能力

攻击模式的服务端强制、性别、玩家死亡掉落、红名。实现时把版本字符串和验收脚本一起改。

| 日期 | 变更 | 版本字符串 |
|------|------|------------|
| 2026-10-30 | 建立本说明。 | 1.21.0 |
| 2026-10-30 | 追加 3901–3910 与组队聊天。不改 `protocol/VERSION`。 | 1.21.0 |
| 2026-10-30 | Phase M：3921–3923 攻击模式与 PK 同步，3931–3932 鉴定，3941–3953 面对面交易。`AttackReq.force`、`EntitySnapshot` 的 pk/名字色、`EquippedSlot` 耐久、`SiegeStatusResp.schedule_note`。版本字符串仍是 1.21.0。 | 1.21.0 |
| 2026-10-30 | Phase N：3961–3964 世界地图传送与挖矿，3967 地面火墙。`CreateCharReq.gender`、交易多格、`GuildInfoResp.notice`。版本字符串仍是 1.21.0。 | 1.21.0 |
| 2026-10-30 | Phase O：没有新 MsgId。疾光/烈火改为直线，日程结束和手动停战共用发奖。 | 1.21.0 |
