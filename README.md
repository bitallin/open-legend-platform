# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M8)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M8（组队邀请加入与好友私聊闭环）

协议版本 **1.8.0**。在 M7 摆摊/交易基础上新增：

| 项 | 约定 |
|----|------|
| 组队 Party | `PartyInvite/Accept/Leave/Kick` + `PartyInfoNotify`（MsgId 2701–2710）；一人同时一支队伍；队长邀请→接受；队长离队转让下一成员否则解散；上限 `maxMembers`（默认 5） |
| 组队经验 | 同图（可配）在线队员均分击杀经验，并按 `expBonusPercent` 加成池；表驱动 `configs/party.json` |
| 好友 Friend | **单向**加好友；`FriendAdd/Remove/List`（2711–2716）；禁止加自己；上限 `maxFriends`；`friends` 表三后端持久化 |
| 私聊 Whisper | `ChatChannel.CHAT_WHISPER` + `ChatReq.target_name`；**必须先是好友**（`whisperRequiresFriend`） |
| Lua 钩子 | `on_party_join` / `on_party_leave` / `on_friend_add` / `on_whisper` |

### M7 / M6 / M5 / M4 / M3 / M2（仍生效）
摆摊交易、行会与三频道聊天、升级曲线与药水、装备槽/强化、NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.8.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 2701–2716 组队/好友
│   ├── Legend.Core/             # VersionPackLoader(+party.json)
│   ├── Legend.Storage/          # friends 三后端持久化
│   ├── Legend.Game/             # GameWorldModule.PartySocial
│   ├── Legend.Script/           # on_party_* / on_friend_add / on_whisper
│   ├── Legend.CliClient/        # --party 双会话验收
│   └── Legend.Tests/            # M0–M8（31 tests）
├── client/unity-sdk/            # RequestParty* / RequestFriend* / RequestWhisper* (1.8.0)
├── version-packs/sample-classic/# party.json + party.lua + social.lua
└── scripts/verify-m8.sh
```

## 2. 快速验证
```bash
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
1. Handshake `1.8.0` → 两账号 Register/Login → CreateChar → EnterWorld
2. A `FriendAdd` B；A `Whisper` B 成功；B 未加 A 时 Whisper A 被拒绝
3. A `PartyInvite` B → B `PartyAccept` → 同图击杀史莱姆，双方均收到 `ExpGainNotify`（带加成均分）
4. B `PartyLeave`；再组队后 A `PartyKick` B
5. 重启 Host 后 A `FriendList` 仍含 B

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --party`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.8.0`；新增 `RequestPartyInviteAsync` / `RequestPartyAcceptAsync` / `RequestPartyLeaveAsync` / `RequestPartyKickAsync`、`RequestFriendAddAsync` / `RequestFriendRemoveAsync` / `RequestFriendListAsync`、`RequestWhisperAsync`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M9
**技能书学习/技能等级与 Buff/Debuff 状态机**，或 **沙巴克/攻城战最小闭环（行会宣战 + 皇宫占领判定）**，或 **邮件/离线私信与好友在线状态推送**。
