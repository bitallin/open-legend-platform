#!/usr/bin/env bash
# 一键创建新区服配置（目录、Sqlite 路径、manifest 绑定、启停说明）
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

REALM_ID="${1:-}"
REALM_NAME="${2:-}"
PORT="${3:-}"
ADMIN_PORT="${4:-}"

if [[ -z "$REALM_ID" ]]; then
  echo "用法: $0 <realm-id> [realm-name] [port] [admin-port]"
  echo "示例: $0 realm-classic-03 '自研先锋三区' 7300 7301"
  exit 1
fi

REALM_NAME="${REALM_NAME:-$REALM_ID}"
# Auto-pick ports if omitted: scan existing
if [[ -z "$PORT" ]]; then
  PORT=7100
  while rg -q "\"port\": $PORT" realms/*.json 2>/dev/null; do PORT=$((PORT+100)); done
fi
if [[ -z "$ADMIN_PORT" ]]; then
  ADMIN_PORT=$((PORT+1))
fi

CFG="realms/${REALM_ID}.json"
DB="data/realms/${REALM_ID}.db"
MANIFEST="version-packs/sample-classic/manifest.json"

mkdir -p realms data/realms
if [[ -f "$CFG" ]]; then
  echo "ERROR: 已存在 $CFG"
  exit 1
fi

cat > "$CFG" << JSON
{
  "realmId": "${REALM_ID}",
  "realmName": "${REALM_NAME}",
  "host": "127.0.0.1",
  "port": ${PORT},
  "adminPort": ${ADMIN_PORT},
  "sqlitePath": "${DB}",
  "manifestPath": "${MANIFEST}",
  "status": "OPEN",
  "versionPackId": "sample-classic-1.21.0"
}
JSON

# Refresh catalog.json
python3 - << PY
import json, os
from pathlib import Path
root = Path("realms")
files = sorted([p.name for p in root.glob("*.json") if p.name != "catalog.json"])
(root / "catalog.json").write_text(json.dumps({"realms": files}, ensure_ascii=False, indent=2) + "\n")
print("catalog updated:", files)
PY

# Touch empty DB dir; Host Initialize creates schema
mkdir -p "$(dirname "$DB")"
: > "${DB}.placeholder"
rm -f "${DB}.placeholder"

echo ""
echo "=== 新区已创建 ==="
echo "配置: $CFG"
echo "库路径: $DB（首次启动 Host 时建表）"
echo "清单: $MANIFEST"
echo ""
echo "启动:"
echo "  dotnet run --project server/Legend.Host -- \\"
echo "    --realm-config $CFG --realm-catalog realms \\"
echo "    --admin-token \"\${LEGEND_ADMIN_TOKEN:-dev-admin}\" --admin-bind 127.0.0.1"
echo ""
echo "停止: Ctrl+C 或 kill Host PID"
echo "并行二区: 再对另一 realm-config 启动一个 Host 进程即可。"
