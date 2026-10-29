#!/usr/bin/env bash
# Build the website: the IDE (src/ArcadeBasic.Web), with the player (src/ArcadeBasic.Player)
# in its player/ folder — File → Export packs the player with a program — and
# player/files.json listing the player's files, since a website can't list a folder.
# Needs the WebAssembly build tools (dotnet workload install wasm-tools).
#
#   src/ArcadeBasic.Web/build-site.sh [out]     (default: publish/site)
#
# .github/workflows/pages.yml publishes the result to GitHub Pages.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$repo/publish/site}"
if [[ -e "$out" && ! -e "$out/.nojekyll" ]]; then
  echo "$out is there already, and isn't a site this script built: not replacing it" >&2
  exit 1
fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

dotnet publish "$repo/src/ArcadeBasic.Web" --configuration Release --output "$work/web"
dotnet publish "$repo/src/ArcadeBasic.Player" --configuration Release --output "$work/player"

rm -rf "$out"
mkdir -p "$out/player"
cp -R "$work/web/wwwroot/." "$out/"
cp -R "$work/player/wwwroot/." "$out/player/"
# A site serves files as they are, so the precompressed .br/.gz copies would never be
# used; and nothing here is for Jekyll (which would hide _framework).
find "$out" \( -name '*.br' -o -name '*.gz' \) -delete
touch "$out/.nojekyll"
(cd "$out/player" && find . -type f ! -name files.json | sed 's|^\./||' | sort |
  python3 -c 'import json, sys; print(json.dumps([line.strip() for line in sys.stdin]))' > files.json)
echo "built $out"
