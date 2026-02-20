#!/usr/bin/env bash
# cleanup.sh
# ===================================================================
# Deletes OLD OpsCopilot resource groups and misplaced resources
# left over from the initial TenantA/TenantB deployment.
#
# Old layout (pre-reset):
#   SubA:  rg-opscopilot-a-{env}-uks  (contained misplaced AOAI)
#   SubB:  rg-opscopilot-b-{env}-uks
#
# New layout (post-reset):
#   SubA:  rg-opscopilot-platform-{env}-uks
#   SubB:  rg-opscopilot-ai-{env}-uks
#
# Usage:
#   ./cleanup.sh \
#     --env dev \
#     --subscription-a b20a7294-6951-4107-88df-d7d320218670 \
#     --subscription-b bd27a79c-de25-4097-a874-3bb35f2b926a \
#     --target platform         # 'platform', 'ai', or 'both'
#     [--dry-run]
# ===================================================================
set -euo pipefail

# ── Defaults ─────────────────────────────────────────────────────────────────
ENV="dev"
REGION="uks"
SUBSCRIPTION_A=""
SUBSCRIPTION_B=""
TARGET="both"   # platform | ai | both
DRY_RUN=false

# ── Argument parsing ──────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case $1 in
    --env)             ENV="$2";            shift 2 ;;
    --region)          REGION="$2";         shift 2 ;;
    --subscription-a)  SUBSCRIPTION_A="$2"; shift 2 ;;
    --subscription-b)  SUBSCRIPTION_B="$2"; shift 2 ;;
    --target)          TARGET="$2";         shift 2 ;;
    --dry-run)         DRY_RUN=true;        shift 1 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

# ── Helpers ───────────────────────────────────────────────────────────────────
log()  { echo "[cleanup] $*"; }
warn() { echo "[cleanup] WARN: $*" >&2; }

rg_exists() {
  local sub="$1" rg="$2"
  az group show --subscription "$sub" --name "$rg" &>/dev/null
}

delete_rg() {
  local sub="$1" rg="$2"
  if rg_exists "$sub" "$rg"; then
    if $DRY_RUN; then
      log "[DRY-RUN] Would delete RG '$rg' in subscription '$sub'"
    else
      log "Deleting RG '$rg' in subscription '$sub' …"
      az group delete \
        --subscription "$sub" \
        --name "$rg" \
        --yes \
        --no-wait
      log "Delete initiated for '$rg' (async — check portal for completion)."
    fi
  else
    log "RG '$rg' not found in '$sub' — nothing to delete."
  fi
}

# ── Platform cleanup (SubA) ───────────────────────────────────────────────────
cleanup_platform() {
  if [[ -z "$SUBSCRIPTION_A" ]]; then
    warn "--subscription-a not provided; skipping platform cleanup."
    return
  fi

  local old_rg="rg-opscopilot-a-${ENV}-${REGION}"
  log "=== Platform (SubA) cleanup — target RG: $old_rg ==="
  delete_rg "$SUBSCRIPTION_A" "$old_rg"
}

# ── AI cleanup (SubB) ─────────────────────────────────────────────────────────
cleanup_ai() {
  if [[ -z "$SUBSCRIPTION_B" ]]; then
    warn "--subscription-b not provided; skipping AI cleanup."
    return
  fi

  local old_rg="rg-opscopilot-b-${ENV}-${REGION}"
  log "=== AI (SubB) cleanup — target RG: $old_rg ==="
  delete_rg "$SUBSCRIPTION_B" "$old_rg"
}

# ── Main ──────────────────────────────────────────────────────────────────────
log "Starting cleanup | env=$ENV target=$TARGET dry_run=$DRY_RUN"

case "$TARGET" in
  platform) cleanup_platform ;;
  ai)       cleanup_ai ;;
  both)     cleanup_platform; cleanup_ai ;;
  *)        echo "Invalid --target '$TARGET'. Use: platform | ai | both"; exit 1 ;;
esac

log "Cleanup complete."
