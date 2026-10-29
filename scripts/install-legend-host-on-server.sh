#!/usr/bin/env bash
# Run ON the Ubuntu 22.04 server as root after unpacking to /opt/legend
set -euo pipefail
APP=/opt/legend
if [[ ! -f "$APP/bin/Legend.Host.dll" ]]; then
  echo "Unpack the tarball to /opt/legend first (bin/Legend.Host.dll missing)."
  exit 1
fi
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y aspnetcore-runtime-8.0 curl
id legend >/dev/null 2>&1 || useradd --system --home "$APP" --shell /usr/sbin/nologin legend
mkdir -p "$APP/data/realms" /var/backups/legend
chown -R legend:legend "$APP"
TOKEN=$(openssl rand -hex 24)
PAY=$(openssl rand -hex 24)
sed -i "s/CHANGE_ME_ADMIN/$TOKEN/" "$APP/legend-host.service"
sed -i "s/CHANGE_ME_PAY/$PAY/" "$APP/legend-host.service"
install -m 644 "$APP/legend-host.service" /etc/systemd/system/legend-host.service
systemctl daemon-reload
systemctl enable --now legend-host
echo "Admin token (save offline): $TOKEN"
echo "Payment secret (save offline): $PAY"
echo "Status:"; systemctl --no-pager status legend-host | head -20
echo "Open firewall TCP 7100 only; keep Admin 7101 local."
