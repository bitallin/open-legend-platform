#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/legend-host-linux-x64"
rm -rf "$OUT"
mkdir -p "$OUT/data/realms" "$OUT/scripts"
dotnet publish "$ROOT/server/Legend.Host/Legend.Host.csproj" -c Release -r linux-x64 --self-contained false -o "$OUT/bin"
cp -a "$ROOT/realms" "$OUT/realms"
cp -a "$ROOT/version-packs" "$OUT/version-packs"
cp -f "$ROOT/scripts/create-realm.sh" "$OUT/scripts/"
cp -f "$ROOT/scripts/install-legend-host-on-server.sh" "$OUT/install-on-server.sh"
cat > "$OUT/legend-host.service" << 'UNIT'
[Unit]
Description=Legend.Host trial realm
After=network.target

[Service]
WorkingDirectory=/opt/legend
Environment=DOTNET_ENVIRONMENT=Production
Environment=LEGEND_ADMIN_TOKEN=CHANGE_ME_ADMIN
Environment=LEGEND_PAYMENT_SECRET=CHANGE_ME_PAY
Environment=LEGEND_PAYMENT_PROVIDER=mock
ExecStart=/usr/bin/dotnet /opt/legend/bin/Legend.Host.dll --bind 0.0.0.0 --realm-id realm-classic-01 --realm-catalog realms --admin-port 7101 --admin-bind 127.0.0.1
Restart=on-failure
RestartSec=5
User=legend
Group=legend
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
UNIT
chmod +x "$OUT/install-on-server.sh" "$OUT/scripts/"*.sh
tar -czf "$ROOT/dist/legend-host-linux-x64.tar.gz" -C "$ROOT/dist" legend-host-linux-x64
ls -lh "$ROOT/dist/legend-host-linux-x64.tar.gz"
