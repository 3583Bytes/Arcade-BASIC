#!/usr/bin/env bash
# Build the netstandard2.1 target of the Arcade BASIC libs and copy the resulting
# DLLs into unity/Runtime/Plugins so Unity can pick them up.
#
# Usage:   ./unity/scripts/copy-dlls.sh
# Run from the repo root.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PUBLISH_DIR="$(mktemp -d)"
TARGET_DIR="$REPO_ROOT/unity/Runtime/Plugins"

trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "::> publishing ArcadeBasic.Interpreter (netstandard2.1) ..."
dotnet publish "$REPO_ROOT/src/ArcadeBasic.Interpreter" \
    --configuration Release \
    --framework netstandard2.1 \
    --output "$PUBLISH_DIR" \
    --nologo \
    --verbosity quiet

# Also publish the VM-side libs so users of the bytecode path can reference them.
echo "::> publishing ArcadeBasic.Vm + ArcadeBasic.Compiler (netstandard2.1) ..."
dotnet publish "$REPO_ROOT/src/ArcadeBasic.Vm" \
    --configuration Release \
    --framework netstandard2.1 \
    --output "$PUBLISH_DIR" \
    --nologo \
    --verbosity quiet
dotnet publish "$REPO_ROOT/src/ArcadeBasic.Compiler" \
    --configuration Release \
    --framework netstandard2.1 \
    --output "$PUBLISH_DIR" \
    --nologo \
    --verbosity quiet

mkdir -p "$TARGET_DIR"

# Wipe previous DLLs + meta files so renamed/removed assemblies don't linger.
find "$TARGET_DIR" -maxdepth 1 \( -name '*.dll' -o -name '*.dll.meta' \) -delete || true

# Portable MD5 helper (Linux uses `md5sum`, macOS uses `md5`).
hash_filename() {
    if command -v md5sum >/dev/null 2>&1; then
        echo -n "$1" | md5sum | head -c 32
    else
        echo -n "$1" | md5 -q
    fi
}

# Emit a Unity plugin importer .meta file next to <dll_path>. We disable
# `validateReferences` so Unity loads the DLL even when its netstandard2.1
# references (System.Memory, System.Buffers, ...) don't bind exactly to
# Unity's bundled BCL assemblies — that strict check is what produces the
# "Reference has errors 'Singulink.Numerics.BigDecimal'" import error.
emit_meta() {
    local dll_path="$1"
    local meta_path="${dll_path}.meta"
    local name guid
    name="$(basename "$dll_path")"
    guid="$(hash_filename "$name")"
    cat > "$meta_path" <<EOF
fileFormatVersion: 2
guid: ${guid}
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 0
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  - first:
      Windows Store Apps: WindowsStoreApps
    second:
      enabled: 1
      settings:
        CPU: AnyCPU
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
}

# Copy the libs we actually want. System.* assemblies shipped by netstandard2.1
# publish output conflict with Unity's built-ins, so we exclude them. Emit a
# .meta file next to every DLL we keep.
for dll in "$PUBLISH_DIR"/*.dll; do
    name="$(basename "$dll")"
    case "$name" in
        System.*)
            echo "::  skip $name (Unity ships its own)"
            ;;
        *)
            cp -v "$dll" "$TARGET_DIR/"
            emit_meta "$TARGET_DIR/$name"
            ;;
    esac
done

echo
echo "::> Unity Runtime/Plugins now contains:"
ls -1 "$TARGET_DIR"
