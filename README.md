# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M10)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M10（沙巴克/据点争夺战最小闭环）

协议版本 **1.10.0**。在 M9 技能书/Buff 基础上新增：

| 项 | 约定 |
|----|------|
| 攻城配置与皇宫区域 | `configs/siege.json`（`sabuk_castle` 沙巴克城，`declareCostGold: 50`，`leaderOnlyDeclare: true`，`captureZone: dark_forest (16,16)-(24,24)`） |
| 攻城报名与状态 | `SiegeDeclareReq/Resp`（2901–2902）、`SiegeStatusReq/Resp`（2903–2904）、`SiegeStateNotify`（2905）、`SiegeControlReq/Resp`（2906–2907）、`SiegeCaptureReq/Resp`（2908–2909） |
| 皇宫区域跨行会 PvP | 攻城开启期间在 `captureZone` 允许跨行会 `AttackReq` 交战（同行会免伤 `ErrSiegeDenied`），击杀敌方送回出生点 `(5,5)` 复活；非攻城区域保持 M2 默认禁 PvP |
| 夺旗占领与城主增益 | 皇宫内有敌对存活成员时夺旗返回 `ErrSiegeContested`；清场后唯一报名行会夺旗成功，挂载 `buff_castle_lord`（+20% 攻防）且结算发放 `winnerRewardGold: 100` |
| 三后端持久化 | `ISiegeStore`（`InMemory` / `Sqlite` / `Postgres` `siege_state` 表）持久化占领行会与报名行会，Host 重启完整保留且登录自动挂载城主增益 |
| Lua 钩子 | `on_siege_declare` / `on_siege_capture` / `on_siege_end`（`scripts/siege.lua`） |
| 验收 | CLI `--siege`；`M10IntegrationTests`；`scripts/verify-m10.sh` |

### M9（技能书学习、技能成长与 Buff 状态机）

| 项 | 约定 |
|----|------|
| 技能书学习 | `LearnSkillReq/Resp`（2801–2802）或 `UseItem` skillbook；职业/等级门槛；商店售 `item_skillbook_fatal_strike` |
| 技能列表同步 | `SkillListReq/Resp/Notify`（2803–2805）；EnterWorld 携带已学技能；施法校验「已学会」 |
| 技能成长 | 施法/击杀累计 `skillExp`，表驱动升技能等级；等级影响伤害倍率与冷却；`skills_json` 三后端持久化 |
| Buff/Debuff | `configs/buffs.json`：`buff_war_cry`（+Atk%）、`debuff_poison`；`BuffApply/RemoveNotify`（2807–2808）；属性重算挂钩 EffectiveAtk/Def；死亡可清 |
| Lua 钩子 | `on_learn_skill` / `on_skill_level_up` / `on_buff_apply` / `on_buff_expire` |
| 验收 | CLI `--skill`；`M9IntegrationTests`；`scripts/verify-m9.sh` |

### M8 / M7 / M6 / M5 / M4 / M3 / M2（仍生效）
组队/好友私聊、摆摊交易、行会与三频道聊天、升级曲线与药水、装备槽/强化、NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.10.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 2801–2808 技能/Buff
│   ├── Legend.Core/             # VersionPackLoader(+buffs.json)
│   ├── Legend.Storage/          # friends 三后端持久化
│   ├── Legend.Game/             # GameWorldModule.PartySocial
│   ├── Legend.Script/           # on_party_* / on_friend_add / on_whisper
│   ├── Legend.CliClient/        # --party 双会话验收
│   └── Legend.Tests/            # M0–M10（35 tests）
├── client/unity-sdk/            # RequestLearnSkill* / SkillList / Buff events (1.9.0)
├── version-packs/sample-classic/# party.json + party.lua + social.lua
└── scripts/verify-m10.sh
```

## 2. 快速验证
```bash
./scripts/verify-m10.sh
./scripts/verify-m9.sh
./scripts/verify-m8.sh
# 回归：
./scripts/verify-m7.sh
./scripts/verify-m6.sh
./scripts/verify-m5.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（组队共享经验 + 好友私聊 + 重启持久化）
1. Handshake `1.9.0` → Register/Login → CreateChar(warrior) → EnterWorld
2. ShopBuy `item_skillbook_fatal_strike` + `item_war_cry_scroll` → LearnSkill → 施法升级 → UseItem 战吼 Buff
3. 重启 Host：已学技能等级经 `skills_json` 保留

旧 M8 步骤参考：
1. Handshake `1.9.0` → 两账号 Register/Login → CreateChar → EnterWorld
2. A `FriendAdd` B；A `Whisper` B 成功；B 未加 A 时 Whisper A 被拒绝
3. A `PartyInvite` B → B `PartyAccept` → 同图击杀史莱姆，双方均收到 `ExpGainNotify`（带加成均分）
4. B `PartyLeave`；再组队后 A `PartyKick` B
5. 重启 Host 后 A `FriendList` 仍含 B

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --party`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.9.0`；新增 `RequestPartyInviteAsync` / `RequestPartyAcceptAsync` / `RequestPartyLeaveAsync` / `RequestPartyKickAsync`、`RequestFriendAddAsync` / `RequestFriendRemoveAsync` / `RequestFriendListAsync`、`RequestWhisperAsync`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M9
**技能书学习/技能等级与 Buff/Debuff 状态机**，或 **沙巴克/攻城战最小闭环（行会宣战 + 皇宫占领判定）**，或 **邮件/离线私信与好友在线状态推送**。
