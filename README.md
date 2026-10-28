# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M7)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M7（玩家交易与安全区摆摊最小闭环）

协议版本 **1.7.0**。在 M6 行会/多频道聊天基础上新增：

| 项 | 约定 |
|----|------|
| 安全区上架 | `StallListReq/Resp`（2601/2602）；校验安全区（`requireSafeZone: true`）、单价范围、单角色最大挂单数（默认 10）；**立即从卖家背包扣除物品并落库**（防复制漏洞） |
| 浏览挂单 | `StallBrowseReq/Resp`（2603/2604）；支持按 `itemIdFilter` 筛选当前 `active` 在售挂单 |
| 原子购买 | `StallBuyReq/Resp`（2605/2606）；禁止自买（`ERR_SELF_BUY_DENIED`）、校验金币充足、CAS 原子标记 `sold` 防并发重复购买；扣买家金币 + 入买家背包 + 给卖家加金币（在线/离线均落库，在线额外推送 `StallSoldNotify` 2609） |
| 主动下架 | `StallCancelReq/Resp`（2607/2608）；仅卖家本人可下架 `active` 挂单，物品退回卖家背包 |
| 持久化 | `trade_listings` 表；`InMemoryStorage` / `SqliteStorage` / `PostgresStorage` 三后端一致；Host 重启后未成交挂单仍在 |
| VersionPack | `configs/trade.json` + `scripts/trade.lua` |
| Lua 钩子 | `on_stall_list` / `on_stall_buy` / `on_stall_cancel` |

### M6 / M5 / M4 / M3 / M2（仍生效）
行会与三频道聊天、升级曲线与药水、装备槽/强化、NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.7.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 2601–2609 摆摊/交易市场
│   ├── Legend.Core/             # VersionPackLoader(+trade.json)
│   ├── Legend.Storage/          # trade_listings 三后端持久化
│   ├── Legend.Game/             # GameWorldModule.Trade（摆摊上架/浏览/购买/下架）
│   ├── Legend.Script/           # on_stall_list / on_stall_buy / on_stall_cancel
│   ├── Legend.CliClient/        # --trade 双会话验收
│   └── Legend.Tests/            # M0–M7（29 tests）
├── client/unity-sdk/            # RequestStall* / OnStallSoldNotify (1.7.0)
├── version-packs/sample-classic/# trade.json + trade.lua
└── scripts/verify-m7.sh
```

## 2. 快速验证
```bash
./scripts/verify-m7.sh
# 回归：
./scripts/verify-m6.sh
./scripts/verify-m5.sh
./scripts/verify-m4.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --pack version-packs/sample-classic/manifest.json --storage Sqlite --sqlite-path data/legend.db
```

### 手动验收（安全区摆摊 + 购买/下架 + 重启持久化）
1. Handshake `1.7.0` → 卖家/买家两账号 Register/Login → CreateChar → EnterWorld
2. 卖家在安全区外尝试 `StallList` → 返回 `ERR_SAFE_ZONE`
3. 卖家回安全区上架 `item_hp_potion` → 背包立即扣除；下架其中一单 → 物品退回背包
4. 买家 `StallBrowse` 查询在售挂单并 `StallBuy` 购买 → 买家扣金得物，卖家收到 `StallSoldNotify` 且金币增加
5. 重启 Host 后未售出挂单仍在，买家可继续购买（卖家离线或在线均正确加金落库）

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --trade`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.7.0`；新增 `RequestStallListAsync` / `RequestStallBrowseAsync` / `RequestStallBuyAsync` / `RequestStallCancelAsync` 及事件 `OnStallSoldNotify`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M8
**组队（Party）与组队经验/掉落分配 + 好友/私聊（Whisper）**，或 **技能书学习/技能等级与 Buff/Debuff 状态机**，或 **沙巴克/攻城战最小闭环（行会宣战 + 皇宫占领判定）**。
