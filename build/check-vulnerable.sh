#!/usr/bin/env bash
# Release and PR gate: fail if any project resolves a package with a known advisory, direct or transitive.
# Parses the JSON output; never trusts the command's exit code.
# It audits the last restore (project.assets.json), so restore the line first:
#   dotnet restore <sln> -p:UmbracoMajor=17 && build/check-vulnerable.sh   (CI and publish.yml loop over eng/lines.txt)
# Usage: build/check-vulnerable.sh [solution-or-project]   (default: the package solution)
set -euo pipefail

TARGET="${1:-src/Umbraco.Community.EmailSender.slnx}"
OUT="$(mktemp)"
trap 'rm -f "$OUT"' EXIT

dotnet list "$TARGET" package --vulnerable --include-transitive --format json > "$OUT" || true

python3 - "$OUT" <<'PY'
import json, sys
path = sys.argv[1]
try:
    data = json.load(open(path, encoding="utf-8-sig"))
except Exception as e:  # unparseable output is a failure, not a pass
    print(f"check-vulnerable: could not parse dotnet output: {e}", file=sys.stderr)
    print(open(path, encoding="utf-8", errors="replace").read(), file=sys.stderr)
    sys.exit(1)
if data.get("problems"):
    for p in data["problems"]:
        print(f"check-vulnerable: problem: {p.get('text')}", file=sys.stderr)
    sys.exit(1)
found = []
for proj in data.get("projects", []):
    for fw in proj.get("frameworks", []) or []:
        for kind in ("topLevelPackages", "transitivePackages"):
            for pkg in fw.get(kind, []) or []:
                for v in pkg.get("vulnerabilities", []) or []:
                    found.append(f"{proj['path']}: {pkg['id']} {pkg.get('resolvedVersion')} {v.get('severity')} {v.get('advisoryurl')}")
if not data.get("projects"):
    print("check-vulnerable: no projects in output", file=sys.stderr)
    sys.exit(1)
if found:
    print("check-vulnerable: vulnerable packages found:", file=sys.stderr)
    for f in found:
        print("  " + f, file=sys.stderr)
    sys.exit(1)
print(f"check-vulnerable: no vulnerable packages in {len(data['projects'])} project(s)")
PY
