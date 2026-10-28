# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M6)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M6（行会创建/加入 + 世界/附近/行会聊天）

协议版本 **1.6.0**。在 M5 升级/用药基础上新增：

| 项 | 约定 |
|----|------|
| 行会创建 | `GuildCreateReq/Resp`（2501/2502）；扣金币（默认 50，可配置）；名称唯一；创建者为会长 |
| 加入/离开 | `GuildJoinReq/Resp`（按名加入）、`GuildLeaveReq/Resp`；一人仅属一个行会；上限默认 20 |
| 行会信息 | `GuildInfoReq/Resp`；EnterWorld 附带 `guildId/guildName/guildRank` |
| 持久化 | `guilds` 表 + 角色 `guild_id`；InMemory / Sqlite / Postgres 一致；重启保留 |
| 聊天频道 | `ChatReq` / `ChatNotify`（2509/2510）：`World` / `Nearby`（Chebyshev，默认 14）/ `Guild` |
| 权威规则 | 长度/冷却限制；未入会不能发行会频道；附近仅同图范围内；世界=本服在线；行会=同行会在线 |
| VersionPack | `configs/guild.json` + `scripts/guild.lua` |
| Lua 钩子 | `on_guild_create` / `on_guild_join` / `on_guild_leave` |

### M5 / M4 / M3 / M2（仍生效）
升级曲线与药水、装备槽/强化、NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.6.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 2501–2510 行会/聊天
│   ├── Legend.Core/             # VersionPackLoader(+guild.json)
│   ├── Legend.Storage/          # guilds + character.guild_id 三后端
│   ├── Legend.Game/             # GameWorldModule.GuildChat（行会+多频道）
│   ├── Legend.Script/           # on_guild_create / on_guild_join / on_guild_leave
│   ├── Legend.CliClient/        # --guild 双会话验收
│   └── Legend.Tests/            # M0–M6（27 tests）
├── client/unity-sdk/            # RequestGuild* / RequestChatAsync；guild 字段同步
├── version-packs/sample-classic/# guild.json + guild.lua
└── scripts/verify-m6.sh
```

## 2. 快速验证
```bash
./scripts/verify-m6.sh
# 回归：
./scripts/verify-m5.sh
./scripts/verify-m4.sh
./scripts/verify-m3.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db \
  --manifest version-packs/sample-classic/manifest.json --port 7100
```

### 手动验收（行会 + 三频道 + 重启持久化）
1. Handshake `1.6.0` → 两账号 Register/Login → CreateChar → EnterWorld
2. A：`GuildCreate("龙腾行会")` 扣 50 金，成为会长；B：`GuildJoin("龙腾行会")`
3. A 发 `ChatWorld` / `ChatNearby` / `ChatGuild`，B 均应收到对应 `ChatNotify`
4. B `GuildLeave` 后再发 `ChatGuild` → `ServerNotice(chat_denied)`
5. 重启 Host 后双方 EnterWorld：`guildId/guildName/guildRank` 仍在；再 Join/Info 成员列表正确

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --guild`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.6.0`；新增 `RequestGuildCreateAsync` / `RequestGuildJoinAsync` / `RequestGuildLeaveAsync` / `RequestGuildInfoAsync` / `RequestChatAsync`；EnterWorld 同步 guild 字段。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M7
**技能学习/快捷栏与 Buff 状态**，或 **交易/摆摊最小闭环**，或 **好友/私聊与邮件**。
