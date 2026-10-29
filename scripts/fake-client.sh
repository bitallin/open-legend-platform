#!/usr/bin/env bash
# Placeholder Unity/player process for launcher smoke tests.
set -euo pipefail
HOST="127.0.0.1"
PORT="7100"
REALM=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --realm-id) REALM="$2"; shift 2 ;;
    *) shift ;;
  esac
done
echo "[fake-client] host=${HOST} port=${PORT} realm-id=${REALM}"
echo "[fake-client] OK"
exit 0
