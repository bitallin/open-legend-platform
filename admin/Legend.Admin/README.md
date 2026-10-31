# Legend.Admin — Open Legend 生产级运营后台

网站形态的传奇私服运营/配置后台（**不是游戏画面**）。用户在此配置版本包内容、区服、爆率、商城、邮件、在线封禁、素材索引等。

规划：[`docs/admin/ADMIN_CONSOLE_PLAN.md`](../../docs/admin/ADMIN_CONSOLE_PLAN.md)  
进度：[`docs/admin/ADMIN_PROGRESS.md`](../../docs/admin/ADMIN_PROGRESS.md)

## 启动

在仓库根目录：

```bash
dotnet run --project admin/Legend.Admin --urls http://127.0.0.1:7200
```

浏览器打开 http://127.0.0.1:7200  

开发种子账号（**仅本地**，上线务必改密）：

| 用户 | 密码 | 角色 |
|------|------|------|
| admin | LegendAdmin!2026 | SuperAdmin |
| operator | LegendOps!2026 | Operator |
| support | LegendSupport!2026 | Support |

Admin SQLite：`data/admin/admin.db`（自动创建）。

## 连接游戏 Host Admin

`appsettings.json` / 环境变量：

- `Legend:HostAdmin:BaseUrl` 默认 `http://127.0.0.1:7101`
- `Legend:HostAdmin:Token` 或 `LEGEND_ADMIN_TOKEN`（与 Host `--admin-token` 一致）
- `Legend:RepoRoot` 可空（自动向上查找含 `version-packs/` 的仓库根）

Host 未启动时，配置编辑/区服/邮件队列/CDK/审计仍可用；在线/封禁/热重载/攻城会显示连接失败。

## 模块一览

内容配置目录与分域列表、玩法数值合集、UI/特效/音效索引、商城与调账补单、区服与合区计划、热重载、在线/封禁、邮件、公告、沙巴克、CDK、操作审计、管理员 RBAC。
