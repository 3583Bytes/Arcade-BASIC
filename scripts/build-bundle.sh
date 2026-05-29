#!/usr/bin/env bash
# Build a self-contained bundle of `arcade-basic` (AOT CLI) and
# `arcade-basic-ide` (self-contained single-file IDE) into one directory.
#
# Drop the resulting directory into your PATH (or just run binaries from it)
# and the IDE's F7 "Build standalone" will find the CLI next to it for use as
# the build stub.
#
# Usage:
#   scripts/build-bundle.sh                  # uses host RID, output goes to out/
#   scripts/build-bundle.sh osx-arm64
#   scripts/build-bundle.sh win-x64 dist
set -euo pipefail

RID="${1:-}"
OUT="${2:-out}"

if [[ -z "$RID" ]]; then
  # Best-effort host RID detection.
  case "$(uname -s)" in
    Linux)  RID="linux-x64" ;;
    Darwin) RID="$([[ "$(uname -m)" == "arm64" ]] && echo osx-arm64 || echo osx-x64)" ;;
    MINGW*|MSYS*|CYGWIN*) RID="win-x64" ;;
    *) echo "Unknown OS '$(uname -s)'; pass RID explicitly, e.g. linux-x64." >&2; exit 1 ;;
  esac
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

rm -rf "$OUT"
mkdir -p "$OUT"

echo "==> Publishing CLI (AOT) for $RID"
dotnet publish src/ArcadeBasic.Cli \
  -c Release \
  -r "$RID" \
  -o "$OUT"

echo "==> Publishing IDE (self-contained single-file) for $RID"
dotnet publish src/ArcadeBasic.Ide \
  -c Release \
  -r "$RID" \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT"

echo
echo "Bundle ready in $OUT/"
ls -lh "$OUT/arcade-basic"* 2>/dev/null || ls -lh "$OUT/" | grep -i arcade
echo
echo "Try it:"
echo "  $OUT/arcade-basic-ide                          # launch the IDE"
echo "  $OUT/arcade-basic run examples/hello.bas       # run a program"
echo "  $OUT/arcade-basic build examples/hello.bas -o hello && ./hello"
