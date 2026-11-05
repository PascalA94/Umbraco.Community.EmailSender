# spikes

Findings that differ from, or add to, the design. Newest last.

## M0 (scaffold, both lines)

- **Layout:** the template's `src/` + `demo/` layout is kept (tests are in `src/<name>.Tests`, not `tests/`), with one demo site that follows `-p:UmbracoMajor`, instead of two demo projects. One project, one lock file per line, same coverage.
- **Lock files:** `packages.17.lock.json` and `packages.18.lock.json` beside every csproj. The floor leg restores them in locked mode.
- **osv-scanner ignores renamed lock files.** It only recognises `packages.lock.json`, so `--recursive` alone scans none of ours. CI passes each one explicitly with `-L packages.lock.json:<path>` (8 files, 62 to 221 packages each; confirmed locally).
- **`dotnet list package --vulnerable` audits the last restore**, not the `-p:UmbracoMajor` argument (it rejects `-p:`). `check-vulnerable.sh` therefore runs after a restore of each line.
- **Latest leg (17.\*):** restores unlocked and the suite passes locally (3 unit and 2 integration tests). The 18.\* latest leg is the same mechanism and is first verified on GitHub.
- **Package reference:** `Umbraco.Cms.Infrastructure` only (plain class library, no Razor, no ASP.NET framework reference).
- **Unit tests follow the package's range, not 17.\*/18.\*:** the unit-test project has no direct Umbraco reference, so the latest leg floats only the integration tests and the demo. Accepted.
- **Check names:** the `osv` job is a reusable workflow, so its check run is named `osv / <job>` and is diff-only on PRs. Read the real names off the first PR before setting the ruleset. `check-vulnerable` is the absolute gate and now runs without waiting for `osv`.
- **Umbraco floor bumps are manual** (Dependabot cannot update the `$(UmbracoRange)` property or the renamed lock files). No `global.json`: CI uses `10.x`.
