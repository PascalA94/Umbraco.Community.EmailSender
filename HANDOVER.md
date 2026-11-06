# Handover

- **State (M0):** scaffolded from `umbracopkg` (`--umbraco-major 18 --with-backoffice false --with-e2e false`) and converted to the `UmbracoMajor` switch. `./scripts/test.sh` passes on 17 and 18 (unit 3/3, integration 2/2 per line, run after the final code edits). The 17.* latest leg restores unlocked and passes locally. `check-vulnerable` is clean on both lines, `scripts/pack.sh` gives 17.0.0-alpha.1 and 18.0.0-alpha.1, and `actionlint`, `shellcheck` and `gitleaks` are clean. The demo site builds on both lines but has not been booted (it needs the unattended password in user-secrets).
- **CI:** CI has **not** run. Do not tag anything before the first green run (plan Task 9).
- **Layout rulings:** the template's `src/` + `demo/` layout is kept (tests in `src/`, not `tests/`). One demo site follows `-p:UmbracoMajor` instead of two. `Umbraco.Cms.Infrastructure` is the only Umbraco reference. The plan's `UmbracoFloat` is replaced by the template's `UmbracoTestVersion`/`UmbracoDemoVersion` overrides.
- **Plan gaps:** `plan.md` has no M0, no "Resolved / Security review / Plan review" sections, and no Notify task. M0 was set to Task 1 with Pascal.
- **Before the repo goes public (Pascal):**
  1. Delete the "Token rules" pointer line in `CLAUDE.md`.
  2. Scrub local paths and private file names from `docs/design.md` (header table) and `docs/plan.md` (spec path, repo path, review folder, clone command).
  3. `grep -rnE "/Users/|~/Documents|~/Projects" . --exclude-dir=.git -I --exclude='*.lock.json'` must print nothing.
  4. Follow `.github/REPO-SETUP.md`. On the first PR, read the real check names (the `osv` job shows as `osv / <job>`) before setting the ruleset.
- **Pascal-only steps:** push, tag, publish, repo visibility, NuGet ID reservation and Trusted Publishing. Provider test credentials go in user-secrets when needed (Task 11).
- **State (M1):** Tasks 2-4 built (options, mapping, sender, client). Both lines pass (unit 87 on 18, 89 on 17; integration 2/2). No provider exists yet, so do not pack or tag.
- **Next session starts at:** Task 5.
- **State (M3):** Tasks 6, 6b, 7 and 8 built: Postmark, ACS and GOV.UK Notify providers, the Email provider health check, leak tests and the docs. Both lines pass (unit 254 on 17, 252 on 18; integration 2/2). Not done: Task 9 (first CI run, Pascal), 10 (final review) and 11 (live exercise).
- **Pascal, before the first release tag (security High from the M3 review):** create the GitHub environment `nuget` with you as required reviewer and tags limited to `release/*`; set Environment `nuget` on the nuget.org Trusted Publishing policy; add a tag ruleset on `refs/tags/release/*`. See `.github/REPO-SETUP.md` step 6.
- **Next session starts at:** Task 10 once Task 9 is green.
