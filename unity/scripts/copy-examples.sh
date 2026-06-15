#!/usr/bin/env bash
# Mirror the repo's /examples/*.bas into the Unity sample's bundled-examples
# folder so the in-game IDE's Open dropdown lists exactly the same programs as
# the CLI and the TUI IDE. /examples is the single source of truth; this script
# makes the Unity copy match it.
#
# For each examples/<name>.bas it writes:
#   unity/Samples~/ArcadeBasic/Resources/ArcadeBasicSamples/<name>.bas
#   unity/Samples~/ArcadeBasic/Resources/ArcadeBasicSamples/<name>.bas.meta
# The .meta gets a deterministic GUID (md5 of the filename) so re-running is a
# no-op and never churns git. The Open dropdown groups by the `@category` tag in
# each file, so no ordering metadata is needed here.
#
# Mirrored files with no counterpart in /examples are removed, EXCEPT the
# Unity-only extras listed in KEEP below (e.g. keys.bas, an input demo with no
# CLI equivalent).
#
# Usage:   ./unity/scripts/copy-examples.sh
# Run from anywhere; paths are resolved relative to the script.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC_DIR="$REPO_ROOT/examples"
DST_DIR="$REPO_ROOT/unity/Samples~/ArcadeBasic/Resources/ArcadeBasicSamples"

# GUID of BasicScriptedImporter (Editor/BasicScriptedImporter.cs.meta); every
# .bas.meta points its `script` at this importer.
IMPORTER_GUID="34cc2fa83738648b5b18e11c3aba87f8"

# Unity-only example files to preserve even though /examples has no counterpart.
KEEP=("keys.bas")

mkdir -p "$DST_DIR"

# Portable 32-hex GUID derived from a string (stable across runs/machines).
guid_for() {
  if command -v md5sum >/dev/null 2>&1; then
    printf '%s' "$1" | md5sum | cut -c1-32
  else
    printf '%s' "$1" | md5 -q | cut -c1-32   # macOS
  fi
}

write_meta() {
  local meta="$1" name="$2"
  cat > "$meta" <<EOF
fileFormatVersion: 2
guid: $(guid_for "$name")
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {fileID: 11500000, guid: $IMPORTER_GUID, type: 3}
EOF
}

# 1. Copy every example in, (re)generating its .meta.
copied=0
for src in "$SRC_DIR"/*.bas; do
  name="$(basename "$src")"
  cp "$src" "$DST_DIR/$name"
  write_meta "$DST_DIR/$name.meta" "$name"
  copied=$((copied + 1))
done

# 2. Drop any stale mirrored .bas that no longer exists in /examples
#    (skip the Unity-only KEEP list).
removed=0
for dst in "$DST_DIR"/*.bas; do
  name="$(basename "$dst")"
  for k in "${KEEP[@]}"; do [ "$name" = "$k" ] && continue 2; done
  if [ ! -f "$SRC_DIR/$name" ]; then
    rm -f "$dst" "$dst.meta"
    removed=$((removed + 1))
  fi
done

echo "Mirrored $copied example(s) into Resources/ArcadeBasicSamples; removed $removed stale file(s); kept ${KEEP[*]}."
