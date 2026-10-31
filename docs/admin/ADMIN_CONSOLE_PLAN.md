# Open Legend 生产级运营后台（Admin Console）产品规划

> 日期：2026-11-01 Asia/Shanghai  
> 仓库：`bitallin/open-legend-platform`  
> 定位：**网站形态**的可配置运营后台；用户自行配置画面/素材/玩法/数值，**不再由助手画游戏原型图**。  
> 对齐：docs/site 00–22、现有 `AdminHttpServer`（M19）、版本包 `version-packs/`、区服 `realms/`。  
> 原则：100% 原创；能力对标经典传奇私服 GM/运营后台体系；**非**破解或兼容第三方闭源引擎。

## 1. 调研摘要（私服后台常见能力 → OLP 落点）

对照公开资料中的「M2/网关/网页 GM/开区合区助手/充值平台/爆率工具」能力种类（仅种类，不移植格式），与 OLP 现有规格映射：

| 私服常见模块 | 典型能力 | OLP 落点 |
|--------------|----------|----------|
| M2 主控参数 | 经验、攻速、负重、全局开关 | `configs/levels.json`、`classes.json`、`internal.json`、伤害/成长参数（数值页） |
| 怪物与爆率 | 属性、刷怪、掉落表 | `monsters.json`、`loot_tables.json`、地图 spawns |
| 装备/物品 | 属性、槽位、极品 | `items.json`、`equipment_slots.json`、`artifacts.json` |
| 技能/合击 | CD、消耗、合击倍率 | `skills.json`、`combos.json`、`heroes.json` |
| 地图权限 | 进出等级、PK、禁挂 | `maps/*.json` flags（见 docs/site 04） |
| NPC/商店/任务 | 对话、价目、任务链 | `npcs.json`、`shops.json`、`quests.json` |
| 商城/充值 | SKU、支付、补单 | `shops`/商城配置 + PaymentStores + Admin recharge |
| 多货币 | 金币/元宝/绑定等 | `currencies.json` + 调账审计 |
| 行会/沙巴克 | 成员、城主、攻城 | `guild.json`、`siege.json` + Admin siege |
| 邮件/公告 | 全服/个人、附件 | `mail.json` + 运营邮件队列 |
| 在线/封禁 | 踢人、禁言、封号 | 现有 `/admin/online|ban|mute` |
| 开区/合区 | 克隆区服、端口、合并计划 | `realms/*.json` + `create-realm` + 合区计划表 |
| 卡密/CDK | 生成、兑换、作废 | Admin 本地 CDK 表（对接领取钩子可后续） |
| 热重载 | 爆率/物品/脚本重载 | `/admin/reload-pack`（docs/site 18） |
| 日志审计 | 登录/交易/充值/GM | Admin 审计库 + 可查询存储日志 |
| 内挂/辅助 | 默认策略、开关 | `client_assist.json`、`internal.json`、`pickup_filters.json` |
| 素材/UI/音效 | 壳资源、特效、BGM | **配置域**（资源清单+版本发布），非画图 |
| 活动/首充/返利 | 活动开关、礼包 | 活动配置表 + 商城礼包 SKU |
| 微端/更新 | 资源包版本 | 登录器 update-manifest + Admin 发布页 |

## 2. 产品目标

1. **可配置一切内容**：画面壳、地图、怪物、装备、技能、特效、音效、NPC、掉落等均可在后台 CRUD / 导入导出 / 版本发布。  
2. **完整运营面**：区服、合区、货币、充值、在线、封禁、邮件、公告、审计。  
3. **生产级可用性**：RBAC、操作审计、校验、热更策略写清、本地一键启动、种子数据可演示闭环。  
4. **与现网引擎衔接**：读写版本包与 realms；对运行中 Host 调用现有 Admin HTTP；试玩服 `150.158.164.90` 可后续对接。

## 3. 信息架构（IA）

```
仪表盘
├── 内容配置
│   ├── 配置目录（按 manifest 文件）
│   ├── 地图 / 怪物 / 掉落 / 物品 / 装备槽
│   ├── 技能 / Buff / 合击 / 英雄 / 宠物 / 坐骑 / 时装
│   ├── NPC / 商店 / 任务 / 副本 / 世界 Boss
│   ├── 职业 / 等级经验 / 天赋 / 神器 / 生肖
│   ├── 行会 / 攻城 / 组队 / 交易 / 回收
│   ├── 内挂与辅助 / 拾取过滤
│   └── 货币定义
├── 画面与素材（配置，非画图）
│   ├── UI 壳与布局清单（对齐 docs/site 19）
│   ├── 特效 / 音效资源索引（对齐 20）
│   └── 资源包版本与发布
├── 商城与经济
│   ├── 商城 SKU / 礼包
│   ├── 充值订单 / 人工补单
│   └── 多货币调账
├── 世界与区服
│   ├── 区服列表 / 开区 / 维护 / 白名单
│   ├── 合区计划
│   └── 版本包热重载
├── 运营动作
│   ├── 在线玩家（踢/禁言/封号）
│   ├── 全服/个人邮件
│   ├── 公告 / 活动
│   ├── 沙巴克控制
│   └── CDK 卡密
├── 记录与审计
│   ├── GM 操作审计
│   ├── 登录 / 充值 / 邮件日志（可查询）
│   └── 导入导出历史
└── 系统
    ├── 管理员与角色（超管/运营/客服）
    ├── 连接设置（Host Admin Token / 绑定）
    └── 关于与文档链接
```

## 4. 角色权限（RBAC）

| 角色 | 代号 | 权限摘要 |
|------|------|----------|
| 超级管理员 | `SuperAdmin` | 全部；改管理员；改连接密钥；删区服；合区执行 |
| 运营 | `Operator` | 内容配置 CRUD、发布热更、商城、活动、邮件、公告、CDK、调账（需二次确认） |
| 客服 | `Support` | 在线查询、踢人/禁言/临时封号、查订单、个人邮件、只读配置 |

默认种子：`admin` / `LegendAdmin!2026`（**仅本地开发**；生产必须改密）。

## 5. 技术架构

| 层 | 选型 |
|----|------|
| 网站 | ASP.NET Core 8 **Razor Pages** + Bootstrap 5（管理台 UI，非游戏画面） |
| 工程路径 | `admin/Legend.Admin/` |
| 后台库 | SQLite（`data/admin/admin.db`）：用户、角色、审计、邮件队列、公告、CDK、合区计划、草稿版本 |
| 配置源 | 文件系统：`version-packs/**`、`realms/**`（仓库相对路径可配置） |
| 运行时控制面 | HTTP 客户端 → Host `AdminHttpServer`（Bearer token） |
| 鉴权 | Cookie Auth + 角色声明 |
| 部署 | `dotnet run --project admin/Legend.Admin`；默认可绑 `127.0.0.1:7200` |

不强制与 Host 同进程，便于单独扩容与权限隔离；Host 侧既有 API 保持兼容并逐步加厚。

## 6. 数据模型（Admin 库）

- `AdminUsers`：Id, Username, PasswordHash, Role, IsActive, CreatedAt  
- `AuditLogs`：Id, Actor, Action, Target, DetailJson, Ip, CreatedAt  
- `ConfigRevisions`：Id, PackId, RelativePath, ContentHash, Note, Actor, CreatedAt  
- `MailJobs`：Id, Scope(All/Account/Character), Target, Title, Body, AttachmentsJson, Status, CreatedAt  
- `Announcements`：Id, Title, Body, StartsAt, EndsAt, IsActive  
- `CdkBatches` / `CdkCodes`：批次、码、奖励 JSON、已用、作废  
- `MergePlans`：Id, SourceRealmIdsJson, TargetRealmId, ScheduleAt, Status, Notes  
- `CurrencyAdjustments`：调账流水  
- `AssetManifests`：UI/音效/特效资源索引版本  

游戏权威运行时数据仍在 Host Storage（Sqlite/Postgres）；Admin 以配置+指令为主。

## 7. API 面

### 7.1 网站自身（需登录）

- `GET/POST` Razor 页面为主  
- JSON：`/api/configs/{pack}/{file}` 读写校验  
- `POST /api/ops/online|ban|mute|kick|siege|reload|recharge` → 代理 Host  
- `GET /api/audit` 查询  

### 7.2 Host 既有（扩展规划）

保留：`/admin/online|ban|mute|siege|reload-pack|recharge|payment/*`  
规划增补（可分期）：`/admin/mail`、`/admin/kick`、`/admin/notice`、`/admin/currency-adjust`、`/admin/whitelist`。  
本里程碑网站侧先落队列与 UI；Host 未实现的接口在 UI 标明「队列/本地，待 Host 接线」。

## 8. 热更策略

| 变更类型 | 生效方式 | 说明 |
|----------|----------|------|
| 版本包 JSON/Lua | `reload-pack` 热加载 | 失败回滚；见 docs/site 18 |
| 区服 JSON | 重启对应 Host 或滚动 | 端口/库路径变更需重启 |
| 封禁/禁言 | 立即 | 已持久化 |
| 直充/补单 | 立即入账 | 写审计 |
| 邮件/公告队列 | 推送或玩家拉取 | Host 接线后实时 |
| 资源包（客户端） | 登录器更新通道 | 与规则热更分离 |

## 9. 页面树（实现清单）

见里程碑；每个模块统一：**列表 → 详情编辑 → 校验 → 保存修订 → 审计**；支持 JSON 导入/导出。

## 10. 里程碑

| 里程碑 | 内容 | 验收 |
|--------|------|------|
| **M0** | 规划文档 + 进度文档 | 本文 + `ADMIN_PROGRESS.md` |
| **M1** | 网站骨架：登录、RBAC、布局、仪表盘、连接设置 | 本地启动可登录 |
| **M2** | 配置中心：manifest 目录、通用编辑器、导入导出、修订历史 | 改 loot/monsters 可保存 |
| **M3** | 内容分域列表页（怪/装/技/图/NPC/店/任务等） | 种子数据可点通 |
| **M4** | 区服/开区/合区计划/热重载 UI | 读写 realms + 触发 reload |
| **M5** | 运营：在线、封禁、邮件、公告、沙巴克、CDK | 代理或队列可演示 |
| **M6** | 经济：货币、商城 SKU、订单/补单、调账 | 种子订单+审计 |
| **M7** | 画面/素材配置索引 + 文档对齐 19/20 | 可维护资源清单 |
| **M8** | README 启动、自检脚本、合入 main | 建议用户验收 |

## 11. 非目标

- 不做游戏内画面绘制 / Avalonia 观感返工（用户已否决）。  
- 不兼容第三方 GM 命令文本或 996 协议。  
- 不在首版把 Admin 端口暴露到登录器区服列表。  
- 不自动对公网试玩服改配，除非用户另行授权。

## 12. 验收口径（交给父代理通知用户时）

1. 规划完整且模块对齐私服常见后台能力。  
2. `dotnet run --project admin/Legend.Admin` 可启动；主要菜单可点。  
3. 种子数据演示：改爆率 → 保存修订 →（可选）热重载；封禁/在线页可操作或清晰提示未连 Host。  
4. 进度文档列出已完成与缺口。  
5. PR 已合入 main。
