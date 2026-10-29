# 合区规则（Legend.Merge）

## 账号
- 以 **username（大小写不敏感）** 为键。
- 目标区已有同名账号：保留目标账号（密码/account_id 不变），源区角色并入该账号。
- 目标区无同名：复制源账号（保留 password_hash 与新/旧 account_id 映射）。

## 角色
- 同账号角色列表合并。
- **角色名冲突**（目标区已存在同名，大小写不敏感）：源角色改名为 `{原名}_m`，若仍冲突则 `{原名}_m2`、`_m3`…
- character_id 若与目标冲突：分配新 GUID，并更新行会成员引用。

## 行会
- 以 **guild name（大小写不敏感）** 为键。
- 目标已有同名行会：**保留目标行会**；源行会改名为 `{原名}_m` / `_m2`… 后迁入。
- 成员 character_id 按映射表改写；leader 按映射改写。
- ranks_json / warehouse_json / 审计流水：随行会迁入（审计 actor id 尽量映射）。

## 其它
- 邮件、好友、寄售、攻城、禁言：本版合区 **迁入源区邮件与 moderation**；好友/寄售/攻城以目标为准（源侧丢弃，避免跨区悬挂引用）。
- 支付订单：不合并（新区重新下单）。

## 用法
```bash
dotnet run --project tools/Legend.Merge -- \
  --source-a data/realms/a.db --source-b data/realms/b.db \
  --target data/realms/merged.db --dry-run

dotnet run --project tools/Legend.Merge -- \
  --source-a a.db --source-b b.db --target merged.db
```
