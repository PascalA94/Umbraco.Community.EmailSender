#!/usr/bin/env bash
# Secret scan with a pinned, checksum-verified gitleaks CLI.
# Usage: build/gitleaks.sh "<base>..<head>"   scan a commit range (PR checks)
#        build/gitleaks.sh ""                 scan staged content (run after `git add -A`, before anything is public).
#                                             Staged, not `dir .`: git-ignored runtime files (demo DB, logs) are never committed.
# Why the CLI and not gitleaks-action: no licence key is needed if the repo moves to an organisation.
set -euo pipefail

VERSION="8.30.1"
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) ASSET="gitleaks_${VERSION}_linux_x64.tar.gz"; SHA256="551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb" ;;
  Darwin-arm64) ASSET="gitleaks_${VERSION}_darwin_arm64.tar.gz"; SHA256="b40ab0ae55c505963e365f271a8d3846efbc170aa17f2607f13df610a9aeb6a5" ;;
  *) echo "gitleaks.sh: unsupported platform $(uname -s)-$(uname -m)" >&2; exit 2 ;;
esac

TOOLS="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/gitleaks-${VERSION}"
if [ ! -x "$TOOLS/gitleaks" ]; then
  mkdir -p "$TOOLS"
  curl -fsSL -o "$TOOLS/$ASSET" "https://github.com/gitleaks/gitleaks/releases/download/v${VERSION}/${ASSET}"
  if command -v sha256sum >/dev/null; then
    echo "$SHA256  $TOOLS/$ASSET" | sha256sum -c -
  else
    echo "$SHA256  $TOOLS/$ASSET" | shasum -a 256 -c -
  fi
  tar -xzf "$TOOLS/$ASSET" -C "$TOOLS" gitleaks
fi

RANGE="${1-}"
# A push to a new branch reports an all-zero "before" SHA; scan the pushed commits only.
if [ -n "$RANGE" ] && [[ "$RANGE" == 0000000000000000000000000000000000000000..* ]]; then
  RANGE="${RANGE#*..}"
  RANGE="-1 $RANGE"
fi

if [ -z "$RANGE" ]; then
  "$TOOLS/gitleaks" git --pre-commit --staged --redact --exit-code 1 .
else
  "$TOOLS/gitleaks" git --redact --exit-code 1 --log-opts="$RANGE" .
fi
