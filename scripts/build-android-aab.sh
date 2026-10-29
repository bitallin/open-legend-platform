#!/usr/bin/env bash
# Thin wrapper: Android App Bundle (Play Store).
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec "$ROOT_DIR/scripts/build-android.sh" --aab "$@"
