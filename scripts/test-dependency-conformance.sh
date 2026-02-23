#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────
# test-dependency-conformance.sh
# Thin wrapper that invokes the PowerShell conformance script via pwsh.
# Supports the same parameters: --format text|json
# ──────────────────────────────────────────────────────────────────────────
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Forward all arguments to the PowerShell script
exec pwsh -NoProfile -NonInteractive -File \
    "$REPO_ROOT/scripts/Test-DependencyConformance.ps1" "$@"
