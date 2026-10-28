#!/usr/bin/env bash
# Compatibility shim — M0 superseded by M1
exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/verify-m1.sh" "$@"
