#!/usr/bin/env bash
# Release gate: the tag release/<minor>.<patch>[-suffix] must equal the PackageMinor/PackagePatch/PackageSuffix in
# Directory.Build.props, so a tag can never publish a different version than the one in the repo.
# Usage: build/check-release-version.sh release/0.0-alpha.1. Run from the repository root.
set -euo pipefail

tag="${1:?usage: check-release-version.sh release/<minor>.<patch>[-suffix]}"
want="${tag#release/}"
props="Directory.Build.props"
get() { sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$props" | head -1; }

got="$(get PackageMinor).$(get PackagePatch)"
suffix="$(get PackageSuffix)"
[ -n "$suffix" ] && got="$got-$suffix"

if [ "$got" != "$want" ]; then
  echo "check-release-version: $props has '$got', tag wants '$want'" >&2
  exit 1
fi
echo "check-release-version: $want"
