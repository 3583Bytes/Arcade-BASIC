#!/usr/bin/env bash
# Build the desktop player: the player page (src/ArcadeBasic.Player) in a Tauri app.
#
#   desktop/player/build.sh                  publish the page (needs the wasm-tools workload), then build
#   desktop/player/build.sh --page <dir>     use a page already published (a site's player/)
#   desktop/player/build.sh --universal      on a Mac, for Apple Silicon and Intel both (needs
#                                            rustup target add aarch64-apple-darwin x86_64-apple-darwin)
#
# Needs Rust. It prints where the player is: put it next to arcade-basic, or name it with
# `arcade-basic build game.bas --player <path>`; desktop/player/add-to-site.sh puts it
# on the website for the IDE's File → Export.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$here/../.."
page="" universal=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --page) page="$2"; shift 2 ;;
    --universal) universal=1; shift ;;
    *) echo "usage: build.sh [--page <dir>] [--universal]" >&2; exit 2 ;;
  esac
done

rm -rf "$here/dist"
if [[ -z "$page" ]]; then
  rm -rf "$here/obj-publish"
  dotnet publish "$repo/src/ArcadeBasic.Player" -c Release -o "$here/obj-publish"
  page="$here/obj-publish/wwwroot"
fi
# Tauri compresses what it embeds itself; the precompressed copies would only add size.
mkdir -p "$here/dist"
(cd "$page" && find . -type f ! -name '*.br' ! -name '*.gz' ! -name files.json | while read -r f; do
  mkdir -p "$here/dist/$(dirname "$f")"; cp "$f" "$here/dist/$f"; done)
rm -rf "$here/obj-publish"

# Cargo from here: it reads .cargo/config.toml from the folder it runs in, not the manifest's.
cd "$here"
if [[ -n "$universal" ]]; then
  for target in aarch64-apple-darwin x86_64-apple-darwin; do
    cargo build --release --target "$target"
  done
  mkdir -p target/universal
  lipo -create -output target/universal/arcade-player \
    target/aarch64-apple-darwin/release/arcade-player target/x86_64-apple-darwin/release/arcade-player
  exe="$here/target/universal/arcade-player"
else
  cargo build --release
  exe="$here/target/release/arcade-player"; [[ -f "$exe.exe" ]] && exe="$exe.exe"
fi
# A Mac player's every half must be signed (by the linker), for File → Export to sign the
# apps it makes from it (src/ArcadeBasic.Web/wwwroot/macsign.js).
if [[ "$(uname)" == Darwin ]]; then
  for arch in $(lipo -archs "$exe"); do
    if ! otool -arch "$arch" -l "$exe" | grep -q LC_CODE_SIGNATURE; then
      echo "build.sh: the $arch player isn't signed, so Export can't sign apps made from it" >&2
      exit 1
    fi
  done
fi
echo "built $exe"
