# Admin Console 进度

> 时区：Asia/Shanghai · 分支：`feat/admin-console-web` · 更新：2026-11-01

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
| NPC/店/任务/地图 | Entities/* | 完成 | 同上 |
| 玩法数值合集 | Entities/Gameplay | 完成 | 经验/职业/内挂等跳转 |
| UI/特效/音效索引 | Assets/* | 完成 | 配置域，非画图 |
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
4. 结构化表单（非 JSON）按字段编辑可后续增强；当前通用 JSON 编辑器 + 分域摘要已覆盖配置闭环。
5. 试玩服 `150.158.164.90` Admin 端口对接需用户授权后再配 Token/防火墙。
6. DataProtection 密钥未加密落盘（本地开发警告）；生产应配置密钥环。

## 是否建议父代理通知用户验收

实现与文档齐备、本地可启动且主菜单点通、已合入 main（PR #44 / `e3e767b`）：**是** — 建议父代理通知用户验收。
