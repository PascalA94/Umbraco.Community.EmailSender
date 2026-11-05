# CLAUDE.md

`Umbraco.Community.EmailSender`: Send every Umbraco email through an HTTP API (Azure Communication Services, Postmark, GOV.UK Notify) instead of SMTP.
Plain class library in `src/Umbraco.Community.EmailSender/`, one codebase for both Umbraco lines.
Design `docs/design.md`, plan `docs/plan.md`, spikes `docs/spikes.md`, where work stopped: `COMMITS-TODO.md`, `HANDOVER.md`.

**Never commit, push or publish.** Pascal commits, pushes, tags and releases.

Token rules and the 14 CI rules: `~/Documents/Projects/Umbraco Packages/README.md`, sections "Token rules" and "Build approach > CI and GitHub rules". (Delete this line before the repo goes public.)

## Lines and versions

- One branch (`main`) builds both lines. `UmbracoMajor` (`17` or `18`, default `18`; `Directory.Build.props`) selects the CMS range (`[17.8.0, 18.0.0)` or `[18.3.0, 19.0.0)`), the `UMBRACO_17` / `UMBRACO_18` define, the lock file (`packages.<line>.lock.json`) and the version prefix. `eng/lines.txt` lists the lines.
- `UMBRACO_17` code is only the obsolete `SendAsync(EmailMessage)` overload. Everything else is shared.
- Version is `<line>.<PackageMinor>.<PackagePatch>[-<PackageSuffix>]`; a `release/<minor>.<patch>[-suffix]` tag packs both lines from one commit. A version bump changes `PackageMinor`/`PackagePatch`/`PackageSuffix` in `Directory.Build.props`; `build/check-release-version.sh` checks the tag against them.
- Floors never go below 17.8.0 / 18.3.0.
- The NuGet ID, aliases, HttpClient names and config keys are permanent once released.

## Commands

```bash
./scripts/test.sh                      # restore, build and test every line in eng/lines.txt; extra args go to dotnet test
./scripts/pack.sh                      # one nupkg per line into ./artifacts (locked restore)
dotnet restore src/Umbraco.Community.EmailSender.slnx --locked-mode -p:UmbracoMajor=17   # one line by hand
# SQL Server integration run: set Tests__Database__DatabaseType=SqlServer and Tests__Database__SQLServerMasterConnectionString
# Demo site: HTTPS only (OpenIddict refuses HTTP)
dotnet user-secrets set "Umbraco:CMS:Unattended:UnattendedUserPassword" "<pw>" --project demo/Umbraco.Community.EmailSender.DemoSite
dotnet dev-certs https && dotnet run --project demo/Umbraco.Community.EmailSender.DemoSite   # https://localhost:44331/umbraco
# After restoring a line: bash build/check-vulnerable.sh (audits that restore; CI loops over both lines)
# Demo on the other line: add -p:UmbracoMajor=17 to the dotnet run/build command
```

## Releasing

- Bump `PackageMinor`/`PackagePatch`/`PackageSuffix` in `Directory.Build.props`.
- `bash build/check-vulnerable.sh` (after restoring each line) and `bash build/check-release-version.sh release/<minor>.<patch>[-suffix]` both exit 0.
- Run the **Release checks** workflow by hand on the release branch (SQL Server integration, and e2e if present). Never on PRs. Re-run after a new Umbraco minor.
- Squash-merge, tag `release/<minor>.<patch>[-suffix]` on the merge commit; `publish.yml` publishes both lines with Trusted Publishing. Then `gh release create` for each line, and mark the newest Umbraco line's release as Latest.
- Stable from the first real public release unless the API is still moving (`.github/REPO-SETUP.md` step 10); after that, no breaking changes within a major.
- "Done" means green on GitHub. The first CI run is its own step with Pascal.

## Keep

- The repo is public: no reviews, plans of record or local paths in it.
- `docs/` holds only `design.md`, `plan.md`, `spikes.md`. Fake test secrets carry `// gitleaks:allow` from their first commit.
