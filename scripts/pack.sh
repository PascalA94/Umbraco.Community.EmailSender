#!/usr/bin/env bash
# Packs one nupkg per Umbraco line in eng/lines.txt from the current commit, into ./artifacts.
# Restores in locked mode, so a lock file that drifted fails the pack.
set -euo pipefail
cd "$(dirname "$0")/.."
proj=src/Umbraco.Community.EmailSender/Umbraco.Community.EmailSender.csproj
rm -rf artifacts
while read -r line; do
  [ -z "$line" ] && continue
  echo "== packing line $line =="
  dotnet restore "$proj" --locked-mode -p:UmbracoMajor="$line" -v q
  dotnet pack "$proj" --no-restore -c Release -p:UmbracoMajor="$line" -o ./artifacts
done < eng/lines.txt
