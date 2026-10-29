#!/usr/bin/env bash
# Put desktop players on the website, for the IDE's File → Export: each into <site>/desktop/,
# named for its OS (arcade-player-macos, arcade-player-windows.exe, arcade-player-linux — told
# apart by their first bytes), with desktop/players.json listing them and desktop/app-Info.plist
# (a Mac app's Info.plist, to fill in). The IDE offers an app for each OS listed.
#
#   desktop/player/add-to-site.sh <site> <player>...
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
if [[ $# -lt 1 ]]; then echo "usage: add-to-site.sh <site> <player>..." >&2; exit 2; fi
site="$1"; shift
mkdir -p "$site/desktop"
cp "$here/app-Info.plist" "$site/desktop/app-Info.plist"
for player in "$@"; do
  case "$(head -c 4 "$player" | od -An -tx1 | tr -d ' \n')" in
    cffaedfe|cefaedfe|cafebabe) name=arcade-player-macos ;;
    4d5a*) name=arcade-player-windows.exe ;;
    7f454c46) name=arcade-player-linux ;;
    *) echo "$player isn't a desktop player for macOS, Windows or Linux" >&2; exit 1 ;;
  esac
  cp "$player" "$site/desktop/$name"
  echo "$player → desktop/$name"
done
python3 - "$site/desktop" <<'PY'
import json, os, sys
folder = sys.argv[1]
names = {'arcade-player-macos': 'macos', 'arcade-player-windows.exe': 'windows', 'arcade-player-linux': 'linux'}
def player(f):
    path = os.path.join(folder, f)
    with open(path, 'rb') as file:
        universal = file.read(4) == bytes.fromhex('cafebabe')    # (a Mac app for Apple Silicon and Intel)
    return {'os': names[f], 'file': f, 'size': os.path.getsize(path), **({'universal': True} if universal else {})}
players = [player(f) for f in sorted(os.listdir(folder)) if f in names]
with open(os.path.join(folder, 'players.json'), 'w') as out:
    json.dump({'players': players}, out, indent=1)
PY
