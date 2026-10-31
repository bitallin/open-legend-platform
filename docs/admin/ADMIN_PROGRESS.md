# Admin Console 进度

> 时区：Asia/Shanghai · 分支：`main` · 更新：2026-11-01（M12 地图刷怪 / 默认包）

| 时间 | 里程碑 | 状态 | 备注 |
|------|--------|------|------|
| 2026-11-01 | M0 规划 | **完成** | `docs/admin/ADMIN_CONSOLE_PLAN.md` |
| 2026-11-01 | M1 骨架 | **完成** | Razor Pages + Cookie RBAC + 仪表盘 + SysAdmin |
| 2026-11-01 | M2 配置中心 | **完成** | Configs 目录/编辑/导入导出/修订 |
| 2026-11-01 | M3 内容分域 | **完成** | Monsters/Loot/Items/Skills/Npcs/Shops/Quests/Maps/Gameplay |
| 2026-11-01 | M4 区服/合区/热更 | **完成** | Realms 列表/编辑、Merge 计划、Ops/Reload |
| 2026-11-01 | M5 运营 | **完成** | Online/Moderation、Mail、Announcements、Siege、CDK |
| 2026-11-01 | M6 经济 | **完成** | Currency 调账、Mall SKU、Orders 补单 |
| 2026-11-01 | M7 素材配置 | **完成** | Assets UI/特效/音效索引编辑 |
| 2026-11-01 | M8 README/自检/合入 | **完成** | PR #44 → `e3e767b` |
| 2026-11-01 | M9 素材上传与外观绑定 | **完成** | 素材库上传/缩略图、怪物/物品/地图/UI 绑定、assets_manifest/ui_skin、客户端 `--assets-pack` |
| 2026-11-01 | M10 内容总览+全 CRUD | **完成** | Content Studio、结构化 CRUD/克隆、素材硬删恢复替换、UI 槽 CRUD、玩法开关、配置文件增删复制 |
| 2026-11-01 | M11 视觉体系重做 | **完成** | Design tokens + 壳层、Studio 素材墙/实体板、卡片列表、素材拖拽、编辑双栏预览、UI 槽位板、登录面板；见 `ADMIN_UI_VISUAL.md`；PR #48 → `945c157` |
| 2026-11-01 | M11 试玩服部署 | **完成** | `/opt/legend-admin` @ 150.158.164.90；Login 200；怪物 CRUD 探针 OK；截图 `docs/admin/screenshots-m11/` |

## 已完成

- 工程：`admin/Legend.Admin/`（ASP.NET Core 8 Razor Pages + EF Core Sqlite + BCrypt）
- 规划对齐私服常见后台模块（开区合区、爆率、NPC/商店、技能、任务、商城充值、多货币、审计、在线封禁、邮件、活动/公告、沙巴克、内挂开关跳转、素材索引）
- 种子账号：`admin` / `operator` / `support`（见 PLAN；仅本地）
- 冒烟（2026-11-01）：`/api/health` OK；登录后下列路由均 **HTTP 200**：
  `/` `/Configs` `/Entities/*` `/Assets` `/Economy/*` `/Realms` `/Realms/Merge`
  `/Players/*` `/Mail` `/Announcements` `/Ops/*` `/Cdk` `/Audit` `/SysAdmin/*`
- README：`admin/Legend.Admin/README.md`

## 启动

```bash
dotnet run --project admin/Legend.Admin --urls http://127.0.0.1:7200
# http://127.0.0.1:7200
# admin / LegendAdmin!2026 （仅开发）
```

Host Admin 代理默认 `http://127.0.0.1:7101`；未启动 Host 时配置/队列/审计仍可用，在线/封禁/热更页会提示连接失败。

## 模块完成表

| 模块 | 页面 | 状态 | 备注 |
|------|------|------|------|
| 登录/RBAC | Account/* · SysAdmin/Users | 完成 | SuperAdmin/Operator/Support |
| 仪表盘 | Index | 完成 | 包/区服/Host 探测摘要 |
| 配置目录+JSON 编辑 | Configs/* | 完成 | 修订写入 Admin 库 |
| 怪/掉落/物品/技能 | Entities/* | 完成 | 摘要表 + 跳转编辑器 |
| 地图刷怪 | Entities/MapSpawns | 完成 | 按地图查看/增删改 spawns；侧栏+地图编辑入口 |
| NPC/店/任务/地图 | Entities/* | 完成 | 同上 |
| 玩法数值合集 | Entities/Gameplay | 完成 | 经验/职业/内挂等跳转 |
| UI/特效/音效索引 | Assets/Catalog | 完成 | JSON 清单（保留） |
| 素材库上传 | Assets/Index·File | 完成 | multipart + 缩略图 + MediaAssets |
| 外观绑定 | Assets/Bind*·UiSkin | 完成 | 怪/物/图/UI 槽位表单 |
| 内容总览 Studio | Studio | 完成 | 计数卡片+图库+实体条+全局搜索 |
| 实体结构化 CRUD | Entities/Edit + 列表 | 完成 | 怪/物/图/NPC/技能/任务/店/掉落 增删改查克隆+缩略图 |
| 素材库增强 | Assets/Index | 完成 | 恢复/硬删/替换/元数据/批量软删 |
| UI 皮肤槽 CRUD | Assets/UiSkin | 完成 | 增删自定义槽+预览 |
| 配置文件 CRUD | Configs | 完成 | 新建/删除/复制 JSON |
| 玩法开关 | Entities/Gameplay | 完成 | client_assist/internal/pickup 结构化开关 |
| 操作指南 | Assets/Guide · ASSET_LOOK_GUIDE | 完成 | 中文闭环说明 |
| 区服/维护状态 | Realms/* | 完成 | 读写 `realms/*.json` |
| 合区计划 | Realms/Merge | 完成 | 本地 MergePlans 表 |
| 热重载 | Ops/Reload | 完成 | 代理 Host reload-pack |
| 在线/封禁/禁言 | Players/* | 完成 | 代理；Host 未连可提示 |
| 邮件队列 | Mail | 完成 | 本地队列；Host 接线标注 |
| 公告 | Announcements | 完成 | Admin 库 |
| 沙巴克 | Ops/Siege | 完成 | 代理 Host siege |
| CDK | Cdk | 完成 | 批次/码本地表 |
| 商城 SKU | Economy/Mall | 完成 | 读写版本包商店/商城 JSON |
| 订单/补单 | Economy/Orders | 完成 | 本地订单+审计；支付占位 |
| 多货币调账 | Economy/Currency | 完成 | 调账流水+审计 |
| GM 审计 | Audit | 完成 | 查询 Admin 审计库 |
| 连接设置 | SysAdmin/Settings | 完成 | 展示 Host BaseUrl（密钥走配置/环境） |

## 已知缺口（不挡本轮验收）

1. Host 侧 `/admin/mail|kick|notice|currency-adjust|whitelist` 尚未加厚；网站已用本地队列/UI，页面标明待接线。
2. 真实支付渠道未接；订单/补单为运营演示闭环。
3. 合区执行为计划表，不自动搬迁角色库（需后续运维脚本）。
4. 结构化表单已覆盖主内容域（M10）；高级 JSON 仍作兜底。八方向 sheet 裁剪器仍未做。
7. 八方向 sheet 帧编辑器（可视化裁剪）未做；当前绑定整图 + 可选 frame 元数据。
5. 试玩服直连 `http://150.158.164.90/`：Caddy 已反代 Admin，但轻量侧 **TCP/80 入站到不了虚机**（本机/公网 curl 均 SYN 后无包；`tcpdump` 空）。需在轻量控制台确认/放行 HTTP 80（或改用 8080 并放行）。临时公网：`legend-admin-tunnel`（cloudflared quick tunnel，URL 见服务器 `/opt/legend-admin/DEPLOYED.txt`，重启会变）。
6. DataProtection 密钥未加密落盘（本地开发警告）；生产应配置密钥环。

## 试玩服部署（2026-11-01 Asia/Shanghai）

| 项 | 值 |
|----|-----|
| SHA | `aa80e19` |
| 路径 | `/opt/legend-admin` |
| systemd | `legend-admin`（`127.0.0.1:7200`）· `legend-admin-tunnel`（cloudflared）· Caddy `:80,:8080`→7200 |
| Host 联调 | `Legend__HostAdmin__*` 自 `legend-host` 的 `LEGEND_ADMIN_TOKEN` 注入（不入库） |
| RepoRoot | `/opt/legend`（共享 version-packs/realms/data/admin） |
| 本机冒烟 | 登录 `admin` 后 `/` `/Configs` `/Realms` 等 200；`7101` 仍仅 localhost |
| 直连公网 | **受阻**（见上）；隧道 URL 见 `DEPLOYED.txt` |

## 是否建议父代理通知用户验收

实现与文档齐备、本地可启动且主菜单点通、已合入 main（PR #44 / `e3e767b`）：**是** — 建议父代理通知用户验收。

## M10 验收（2026-11-01 Asia/Shanghai）

- 本地冒烟：登录后 `/Studio` `/Entities/*` `/Entities/Edit` `/Assets` `/Assets/UiSkin` `/Configs` `/Entities/Gameplay` 均 200；怪物创建→改→克隆→删闭环 OK。
- 合入后更新 SHA / 试玩服部署见本节下方或 DEPLOYED.txt。
- **建议父代理通知用户验收：是**


## M10 试玩服部署（2026-11-01 Asia/Shanghai）

| 项 | 值 |
|----|-----|
| SHA | `aa80e19`（PR #47 squash） |
| 路径 | `/opt/legend-admin` |
| 冒烟 | 本机 7200：`/Studio` `/Entities/Monsters` `/Assets` `/Entities/Edit` `/Configs` `/Entities/Gameplay` `/Assets/UiSkin` 200；怪物创建/删探针 OK |
| 公网 | `http://150.158.164.90/`（需轻量放行 80）；隧道见 `legend-admin-tunnel` / `DEPLOYED.txt` |


## M11 验收（2026-11-01 Asia/Shanghai）

- 视觉：生产级 ops-console（图像优先、卡片主视图、石金深色壳层）；规范见 `docs/admin/ADMIN_UI_VISUAL.md`
- 冒烟：登录后 `/` `/Studio` `/Entities/Monsters` `/Assets` `/Assets/UiSkin` `/Entities/Edit` `/Account/Login` 200；怪物 CRUD 仍可用
- 截图：`docs/admin/screenshots-m11/`
- **建议父代理通知用户验收：是**


## M12 地图刷怪 / 默认版本包（2026-11-01 Asia/Shanghai）

**根因**：侧栏「怪物」未带 `packId`，`ListPacks().FirstOrDefault()` 落到空的 `sample-176`，列表为空（不是滤掉无外观）。

**改动**：
- `PackPreference`：显式 query → cookie → **优先 `sample-classic`**（避免字母序空包）
- `/Entities/MapSpawns`：按地图 CRUD `monsters.json` → `spawns`（怪/坐标/数量）
- 侧栏「地图刷怪」+ 地图编辑页入口；实体列表空态提示换包
- 种子：`sample-classic` 18 模板 / 8 地图差异化 27 刷怪点

- 冒烟：登录后 `/Entities/Monsters` 非空；`/Entities/MapSpawns?mapId=novice_plain` 与 `dark_forest` 刷怪不同；增删刷怪点 OK
- 试玩服部署：`/opt/legend-admin` + `sample-classic/configs/monsters.json` @ **`12d7572`**（PR #49）；Login 200；怪物页有 18 条；地图刷怪 CRUD 探针 OK
- **建议父代理通知用户验收：是**

