#!/usr/bin/env bash
# Run in a private MSYS2 installation; restore the exact tested build packages.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
cache="$root/Native/build-media/mingw-packages"
mkdir -p "$cache"
archives=()
while IFS=$'\t' read -r hash name url; do
    [[ $hash == \#* || -z $hash ]] && continue
    archive="$cache/$name"
    [[ -f "$archive" ]] || curl --fail --location --retry 3 "$url" -o "$archive"
    echo "$hash  $archive" | sha256sum -c -
    archives+=("$archive")
done < "$root/scripts/media-mingw-packages.tsv"
pacman -U --noconfirm "${archives[@]}"
