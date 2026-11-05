#!/usr/bin/env bash
# Runs the whole suite once per Umbraco line in eng/lines.txt. Extra arguments go to `dotnet test`
# (for example: scripts/test.sh --filter "FullyQualifiedName~OutboundEmail").
# Each line restores at its own lock file; `Passed!` (Failed: 0) must print once per line.
set -euo pipefail
cd "$(dirname "$0")/.."
sln=src/Umbraco.Community.EmailSender.slnx
while read -r line; do
  [ -z "$line" ] && continue
  echo "== Umbraco line $line =="
  # Locked: a drifted lock file fails here, in the publish job as well as locally. Regenerate with a plain restore.
  dotnet restore "$sln" --locked-mode -p:UmbracoMajor="$line" -v q
  dotnet build "$sln" -c Release --no-restore -p:UmbracoMajor="$line" -v q
  dotnet test "$sln" -c Release --no-build -p:UmbracoMajor="$line" --logger "console;verbosity=minimal" "$@"
done < eng/lines.txt
