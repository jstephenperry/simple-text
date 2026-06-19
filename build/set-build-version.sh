#!/usr/bin/env bash
#
# Stamps an auto-incrementing build number into the project files that carry a literal version,
# for a single CI build. The 3-part semantic version in version.txt remains the single source of
# truth (managed by build/bump-version.sh); this only sets the 4th "build" component so every CI
# build is uniquely and monotonically versioned.
#
#   build/set-build-version.sh <build-number>      # e.g. build/set-build-version.sh "$GITHUB_RUN_NUMBER"
#
# The .NET projects read version.txt via Directory.Build.props and take the build number from
# -p:BuildNumber, so they need no file edit. The only file with a hard-coded version is the WinUI
# MSIX manifest, whose <Identity Version> requires a 4-part value; this rewrites that attribute to
# "<version.txt>.<build-number>" (matching the element build/bump-version.sh keeps in sync).
set -euo pipefail

build="${1:-}"
if [[ ! "$build" =~ ^[0-9]+$ ]]; then
  echo "usage: $0 <build-number>" >&2
  exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
file="$root/version.txt"
manifest="$root/SimpleText.WinUI/Package.appxmanifest"

ver="$(tr -d '[:space:]' < "$file")"
if [[ ! "$ver" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "error: version.txt must hold a 3-part version (found '$ver')" >&2
  exit 1
fi

full="${ver}.${build}"

# Match the same <Identity Version="..."> attribute that bump-version.sh syncs: only the <Identity>
# element has a bare Version= at the start of a line, so this targets it without touching namespaces.
sed -i -E "s#(^[[:space:]]*Version=\")[0-9]+(\.[0-9]+){1,3}(\")#\1${full}\3#" "$manifest"

echo "build version: ${full}  (version.txt ${ver} + build ${build}; stamped into MSIX manifest Identity)"
