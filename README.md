# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M16)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M16（宠物召唤跟随与轻量战斗协助）

协议版本 **1.16.0**。在 M15 坐骑/时装之上新增宠物召唤跟随与轻量战斗协助：`PetSummon`/`Dismiss`、AOI `ENTITY_PET`、主人攻击后宠物对同目标 `pet_assist` 权威伤害；**主人死亡自动收回**（清除 `ActivePetId`，复活后需手动重召）；**断线保留** `ActivePetId`，重进自动再召唤实体。三后端 `appearance_json` 持久化 `ownedPets`/`activePetId`。

| 项 | 约定 |
|----|------|
| 宠物配置 | `configs/pets.json`；示例 `pet_wolf_pup`（跟随距离 2、协助伤害 25%、冷却 1200ms、范围 3） |
| 获得途径 | 商店购买 `item_pet_wolf_pup`（30 金）→ `UseItem` 解锁（`kind=pet_unlock`） |
| 协议 | `PetStatus/Summon/Dismiss`（3501–3506）、`PetSyncNotify`（3511）；`EntityKind.ENTITY_PET=4`；`EntitySnapshot.pet_id` |
| 跟随 | 主人移动后宠物 Chebyshev 靠近；跨图时瞬移到主人旁 |
| 协助 | 主人命中怪物后宠物对同一目标 `skillId=pet_assist`，伤害 `floor(owner.Atk * percent/100)` 再走防御公式 |
| 死亡规则 | 主人死亡：despawn + 清除 `ActivePetId`（须复活后重召）。断线：仅 despawn 实体，保留 `ActivePetId` |
| 持久化 | `appearance_json` 扩展 `{ownedPets,activePetId}`（与坐骑/时装同字段存储） |
| 错误码 | `ERR_PET_DENIED=1074` |
| VersionPack / Lua | `pets.lua`：`on_pet_summon` / `on_pet_dismiss` |
| 验收 | CLI `--pet`；`M16IntegrationTests`；`scripts/verify-m16.sh` |

### M15（坐骑与时装外观同步）

| 项 | 约定 |
|----|------|
| 协议 | Mount/Fashion/AppearanceSync（3401–3413）；`ERR_MOUNT/FASHION/MOVE_TOO_FAST` |
| 验收 | CLI `--mount`；`verify-m15.sh` |

### M14 / M13 / …

世界 Boss、组队副本、邮件、攻城等仍生效。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.16.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 3501–3511 宠物；3401–3413 坐骑/时装
│   ├── Legend.Core/             # VersionPackLoader(+pets/mounts/fashions.json)
│   ├── Legend.Game/             # GameWorldModule.Pets 召唤/跟随/协助/死亡收回
│   ├── Legend.Storage/          # appearance_json 三后端
│   ├── Legend.Script/           # on_pet_summon / on_pet_dismiss
│   ├── Legend.CliClient/        # --pet / --mount 验收
│   └── Legend.Tests/            # M0–M16
├── client/unity-sdk/            # Pet/Mount/Fashion MsgId (1.16.0)
├── version-packs/sample-classic/# pets.json + pets.lua + mounts/fashions
└── scripts/verify-m16.sh
```

## 2. 快速验证
```bash
./scripts/verify-m16.sh
# 回归：
./scripts/verify-m15.sh
./scripts/verify-m14.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（宠物）
1. Handshake `1.16.0` → Register/Login → CreateChar → EnterWorld（两名客户端同图）
2. 商店购买 `item_pet_wolf_pup` → `UseItem` 解锁 → `PetStatus` 见 owned
3. `PetSummon pet_wolf_pup` → 本端与旁观者收到 `EntitySpawn(ENTITY_PET)` / `PetSyncNotify`
4. 主人移动数格 → 宠物跟随靠近（`MoveSync` reason=`PetFollow`）
5. 主人 `Attack` 附近怪物 → 收到宠物 `CombatSync(skillId=pet_assist)` 轻量伤害
6. `PetDismiss` 收回；再次召唤后下线再进：`PetStatus.activePetId` 仍在且实体自动再召唤
7. （规则）主人死亡会清除 active 并 despawn；复活后需手动 `PetSummon`

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --pet`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.16.0`；`RequestPetStatus/Summon/Dismiss`、`OnPetSync`；`EntitySnapshot.PetId`；Mount/Fashion 同前。


## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
