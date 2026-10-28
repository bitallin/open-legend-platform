# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M1)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。

## 当前里程碑：M1（持久化 + 角色 + 地图阻挡/安全区/传送门）

协议版本 **1.1.0**。存储后端：`InMemory` / `Sqlite`（无 Docker 默认）/ `PostgreSQL` + `Redis`（`ops/docker-compose.yml`）。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.1.0)
├── server/
│   ├── Legend.Contracts/        # 接口 + 帧编解码 + MapConfig(阻挡/安全区/传送门)
│   ├── Legend.Core/             # EventBus / Plugin / VersionPackLoader
│   ├── Legend.Storage/          # 账号 PBKDF2、角色、会话 (InMemory/Sqlite/Postgres+Redis)
│   ├── Legend.Gate/             # TCP 网关
│   ├── Legend.Login/            # Handshake / Register / Login / Char CRUD
│   ├── Legend.Game/             # 多地图 AOI、阻挡、安全区、传送门、坐标落库
│   ├── Legend.Script/           # MoonSharp HardSandbox
│   ├── Legend.Host/             # 启动宿主 (--storage / --sqlite / env)
│   ├── Legend.CliClient/        # M1 全链路冒烟客户端
│   └── Legend.Tests/            # M1 集成测试
├── client/unity-sdk/            # Unity 2022 LTS SDK（登录/选角/移动/Gizmos）
├── version-packs/sample-classic/# 双地图 + 安全区 + 传送门
├── tools/Legend.MapValidate/    # 地图校验 CLI
├── ops/docker-compose.yml       # Postgres 16 + Redis 7
└── scripts/verify-m1.sh         # 一键验收（兼容 verify-m0.sh）
```

## 2. 快速验证
```bash
./scripts/verify-m1.sh
```

### 启动 Postgres / Redis（可选，有 Docker 时）
```bash
cd ops && docker compose up -d postgres redis
export LEGEND_STORAGE=Postgres
export LEGEND_PG='Host=127.0.0.1;Port=5432;Database=legend_zone_01;Username=legend;Password=legend_dev_password'
export LEGEND_REDIS=127.0.0.1:6379
dotnet run --project server/Legend.Host -- --manifest version-packs/sample-classic/manifest.json --port 7100
```

无 Docker 时默认 Sqlite：
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db --manifest version-packs/sample-classic/manifest.json
```

## 3. Unity 接入
见 `client/unity-sdk/README.md`（Unity 2022 LTS，导入 Runtime，挂 `LegendWorldDemoBehaviour`）。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
