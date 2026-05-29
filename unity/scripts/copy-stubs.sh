#!/usr/bin/env bash
# AOT-publish the `arcade-basic` CLI and stage the resulting binary into
# unity/Samples~/InGameConsole/Stubs/ under its RID-tagged filename so the
# in-game IDE's "Build standalone" feature finds the stub automatically.
#
# Usage:
#   unity/scripts/copy-stubs.sh              # build for the host RID only (fast)
#   unity/scripts/copy-stubs.sh --all        # build for all four shipped RIDs
#   unity/scripts/copy-stubs.sh osx-arm64    # build for one specific RID
#
# CI invokes `--all` before zipping the Unity package; local dev workflows
# can use the default host-only mode.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STUBS_DIR="$REPO_ROOT/unity/Samples~/InGameConsole/Stubs"
mkdir -p "$STUBS_DIR"

# The stub binary's bytecode-deserializer version and the Plugins DLL's
# bytecode-serializer version MUST match (it's the same code in both, but
# baked into different artifacts). Rebuild the netstandard2.1 Plugins DLLs
# in lockstep with every stub build so the in-Editor IDE and the AOT stub
# never go out of sync.
echo "==> refreshing unity/Runtime/Plugins DLLs (keep in lockstep with the stub)"
"$REPO_ROOT/unity/scripts/copy-dlls.sh"
echo

MODE="${1:-host}"

declare -a RIDS
case "$MODE" in
  host)
    case "$(uname -s)" in
      Linux)  RIDS=("linux-x64") ;;
      Darwin) RIDS=("$([[ "$(uname -m)" == "arm64" ]] && echo osx-arm64 || echo osx-x64)") ;;
      MINGW*|MSYS*|CYGWIN*) RIDS=("win-x64") ;;
      *) echo "Unknown host '$(uname -s)'; pass RID explicitly." >&2; exit 1 ;;
    esac
    ;;
  --all|all)
    RIDS=("linux-x64" "osx-arm64" "osx-x64" "win-x64")
    ;;
  *)
    RIDS=("$MODE")
    ;;
esac

for RID in "${RIDS[@]}"; do
  echo "==> publishing arcade-basic for $RID"
  TMP="$(mktemp -d)"
  dotnet publish "$REPO_ROOT/src/ArcadeBasic.Cli" \
    -c Release -r "$RID" -o "$TMP" --nologo --verbosity quiet
  EXT=""; [[ "$RID" == "win-x64" ]] && EXT=".exe"
  cp "$TMP/arcade-basic$EXT" "$STUBS_DIR/arcade-basic-$RID$EXT"
  rm -rf "$TMP"
  echo "  -> $STUBS_DIR/arcade-basic-$RID$EXT"
done

echo
echo "Done. Stubs ready in:"
echo "  $STUBS_DIR"
ls -lh "$STUBS_DIR"
