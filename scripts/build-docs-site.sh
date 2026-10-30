#!/usr/bin/env bash
# Build the DocFX spec site at docs/site/_site.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${DOTNET_ROOT}:${DOTNET_ROOT}/tools:${PATH}"
fi
export PATH="${HOME}/.dotnet/tools:${PATH}"
if ! command -v docfx >/dev/null 2>&1; then
  dotnet tool install -g docfx --version 2.78.3
fi
docfx build "${ROOT}/docs/site/docfx.json"
echo "Site written to ${ROOT}/docs/site/_site/index.html"
