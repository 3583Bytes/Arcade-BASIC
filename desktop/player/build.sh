#!/usr/bin/env bash
# Build the desktop player for this computer: publish the player page
# (src/ArcadeBasic.Player — needs the wasm-tools workload) into dist/, then build the
# Tauri app around it (needs Rust). The result is target/release/arcade-player; put it
# next to arcade-basic, or name it with `arcade-basic build game.bas --player <path>`.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$here/../.."

rm -rf "$here/dist" "$here/obj-publish"
dotnet publish "$repo/src/ArcadeBasic.Player" -c Release -o "$here/obj-publish"
# Tauri compresses what it embeds itself; the precompressed copies would only add size.
rsync -a --exclude '*.br' --exclude '*.gz' "$here/obj-publish/wwwroot/" "$here/dist/"
rm -rf "$here/obj-publish"

cargo build --release --manifest-path "$here/Cargo.toml"
echo "built $here/target/release/arcade-player"
