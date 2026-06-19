#!/usr/bin/env bash
#
# Bumps the single-source application version and keeps the WinUI MSIX manifest in sync.
#
#   build/bump-version.sh [patch|minor|major]   # bump a component (default: patch)
#   build/bump-version.sh set 2.3.4             # set an explicit 3-part version
#
# version.txt (repo root) is the source of truth: every .NET project reads it via
# Directory.Build.props, and the release pipeline packs the Avalonia/Velopack release from it.
# Windows requires a 4-part package version, so the manifest's <Identity Version> is set to
# "<version>.0" here.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
file="$root/version.txt"
manifest="$root/SimpleText.WinUI/Package.appxmanifest"

cur="$(tr -d '[:space:]' < "$file")"
if [[ ! "$cur" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "error: version.txt must hold a 3-part version (found '$cur')" >&2
  exit 1
fi
IFS='.' read -r major minor patch <<< "$cur"

case "${1:-patch}" in
  major) major=$((major + 1)); minor=0; patch=0 ;;
  minor) minor=$((minor + 1)); patch=0 ;;
  patch) patch=$((patch + 1)) ;;
  set)
    new="${2:-}"
    if [[ ! "$new" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
      echo "usage: $0 set <major.minor.patch>" >&2
      exit 1
    fi
    major="${new%%.*}"; rest="${new#*.}"; minor="${rest%%.*}"; patch="${rest##*.}"
    ;;
  *) echo "usage: $0 [patch|minor|major|set <x.y.z>]" >&2; exit 1 ;;
esac

new="$major.$minor.$patch"
printf '%s\n' "$new" > "$file"

# Sync the MSIX 4-part Identity version. Only the <Identity> element has a bare Version=
# attribute at the start of a line, so this targets it without touching namespace URLs.
sed -i -E "s#(^[[:space:]]*Version=\")[0-9]+(\.[0-9]+){1,3}(\")#\1${new}.0\3#" "$manifest"

echo "version: $cur -> $new   (MSIX manifest ${new}.0)"
