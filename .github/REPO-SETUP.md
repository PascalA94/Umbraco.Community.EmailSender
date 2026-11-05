# Repository setup (do once, then delete this file if you like)

Do these in order, before the first push that runs Actions.

1. **Pre-public checks:** also scrub local paths and private file names from `docs/design.md` (header table) and `docs/plan.md` (spec path, repo path, review folder, clone command). `bash build/gitleaks.sh ""` over staged content, and `git log -p | grep` for local paths. Delete `src/Umbraco.Community.EmailSender/CredentialHelper/README.md` if it names a local path, and the "Token rules" line in `CLAUDE.md` (it names a local path).
2. **Create the repo and make it public** (private repos get limited free minutes and the queue stalls):
   `gh repo create PascalA94/Umbraco.Community.EmailSender --public --source . --disable-wiki`
3. **About** (repo page, gear icon): description = the package description; website = `https://www.nuget.org/packages/Umbraco.Community.EmailSender`; topics = `umbraco`, `umbracopackage`, `dotnet` plus package-specific ones.
4. **Settings, General, Pull Requests:** allow squash merging only; default the commit message to the PR title and description.
5. **Settings, Code security:** turn on Dependabot alerts, Dependabot security updates, secret scanning and push protection, private vulnerability reporting (`SECURITY.md` relies on it), and **CodeQL default setup**.
   - CodeQL's `cs/web/missing-token-validation` on Management API POST endpoints is a false positive. Core's Management API uses no anti-forgery tokens; auth is the backoffice token cookie, and bodies are JSON-only (keep a 415 test for form posts). Dismiss it as "false positive" with that reason (GitHub allows 280 characters). Never add `[ValidateAntiForgeryToken]`: it breaks the backoffice.
6. **NuGet Trusted Publishing:** on nuget.org, add a trusted publishing policy for repository `PascalA94/Umbraco.Community.EmailSender`, workflow file `publish.yml`. Then add the repository **secret** `NUGET_USER` (your nuget.org profile name, not an API key). No long-lived API key is used.
7. **Ruleset on the release branches** (`main`, and any other line such as `v17`): pull request required (squash only), the checks `build (17, floor)`, `build (17, latest)`, `build (18, floor)`, `build (18, latest)`, `secrets`, `osv` and `deps` required, and no force-push or deletion.
8. **First CI run (its own step):** push, open a PR, then run Release checks by hand. Expect rounds of fixes; do not tag before it is green.
9. **Presentation, before the first release:**
   - replace `assets/icon.svg` and re-render `assets/icon.png` at 128x128 (the placeholder ships otherwise);
   - add 2-3 screenshots (see `assets/screenshots/README.md`), list them in `umbraco-marketplace.json` → `Screenshots`, and un-comment the screenshot line in `README.md`;
   - make the NuGet `PackageTags`, the marketplace `Tags` and the GitHub topics describe what the package is.
10. **Versioning:** the package major follows Umbraco's major. Go stable (`18.0.0`) at the first real public release unless settings, API values, aliases or table names are still likely to change; alphas put people off (NuGet hides them). After stable, no breaking changes until the next Umbraco major, and new features ship as minors. An alpha nobody downloaded can be retired: `gh release delete <tag> --cleanup-tag`, and unlist it on nuget.org (NuGet never deletes).
11. **Each release:** run Release checks on the release PR, squash-merge, tag `release/<minor>.<patch>[-suffix]` on the merge commit (`publish.yml` publishes both lines from it, for example `release/0.0-alpha.1` gives 17.0.0-alpha.1 and 18.0.0-alpha.1), `gh release create` for each line, and mark the newest Umbraco line's release as Latest (`gh release edit <tag> --latest`). Icons, tags and README text reach NuGet only with a new version.
12. **Actions housekeeping:** after merges and releases, delete old runs. Keep the latest green Build and Test and CodeQL per release branch, plus every successful Publish run. Never delete runs that belong to an open PR: that removes its required checks and blocks the merge.
