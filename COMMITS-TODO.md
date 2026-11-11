# Commits to make

Pascal commits once per milestone. Plan checkpoints are logged here as a record.

## Checkpoint log

- [x] Task 1 checkpoint: `chore: scaffold v18 line, demo site and CI` (both lines, via the `UmbracoMajor` switch)
- [x] Task 2 checkpoint: `feat: options and provider-gated composer`
- [x] Task 3 checkpoint: `feat: normalise EmailMessage to OutboundEmail`
- [x] Task 4 checkpoint: `feat: IEmailSender and client that bypass the SMTP gate`
- [x] Task 5 checkpoint: `feat: Postmark provider`
- [x] Task 6 checkpoint: `feat: Azure Communication Services provider`
- [x] Task 6b checkpoint: `feat: GOV.UK Notify provider`
- [x] Task 7 checkpoint: `feat: provider health check and leak tests`
- [x] Task 8 checkpoint: `docs: README, Marketplace and release workflow` (Step 5, reserving the NuGet ID, is Pascal's)

- [x] Task 9: CI green on GitHub (Build and Test and Release checks, run by Pascal)
- [x] Task 10 checkpoint: `fix: final review findings`
- [x] M5: icon, screenshots and GitHub repo settings (PR #1 merged 2026-11-11)
- [ ] Task 11: live exercise on 17 and 18

## Paused here (2026-11-11, M5: presentation and repo settings)

Milestone: PR #1 merged to `main`, all seven checks green, so the M4 fixes have run on GitHub. Icon and two screenshots added and wired (REPO-SETUP step 9). GitHub settings for steps 3 to 7 applied: `nuget` environment, `release tags` and `main` rulesets, security features, CodeQL, squash-only merging.
Next task: **Task 11 (live exercise on 17 and 18)**, still needing Postmark, ACS and Notify credentials in the demo's user-secrets.
Open before the first tag: nuget.org Trusted Publishing policy; dismiss the CodeQL `cs/web/missing-token-validation` false positive; alpha or stable; `build/check-vulnerable.sh` and `build/check-release-version.sh`; the Release checks run by hand.
Still undecided (Pascal): whether to untrack `HANDOVER.md` and `COMMITS-TODO.md` from the public repo.

## Paused here (2026-11-06, M4: Task 10 final review)

Milestone: Task 10 done. Full qa-reviewer (opus) and security-analyst (opus) reviews of the whole branch: no Critical or High. Fixed with a failing test first: Notify formatter link-marker forging via `&#xE000;N&#xE001;` (found by both; the formatter now cleans text between anchors and appends links, so no placeholder exists); ACS connection string validated at start-up (shape only, never echoed); booted-host integration tests (`HostWiringTests`: no provider leaves core services; Postmark with no `Smtp:Host` replaces sender and client and `CanSendRequiredEmail` is true; mutation-checked).
Tests: `./scripts/test.sh` passes on both lines (unit 262 on 17, 260 on 18; integration 4/4 each). `check-vulnerable` clean on both. CI was green before this milestone; the fixes have not run on GitHub yet.
Rulings: the plan's demo-site smoke step for `release-checks.yml` is not added (Task 11 covers it live). Minor and Low findings are deferred, not fixed. Cost if wrong: a few polish items ship in 0.x.
Next task: **Task 11 (live exercise on 17 and 18)**. Needs from Pascal, in user-secrets on the demo site: Postmark server token (or `POSTMARK_API_TEST`), ACS connection string with a verified sender domain, GOV.UK Notify trial-mode key and template. Check first that ACS and Postmark accept the `Expires` header. HANDOVER.md is written in full after Task 11.
Pascal decides (not blocking):
1. Local paths (OS username, a private review folder path) remain in the git history of the already-public repo: accept, or rewrite before the first release.
2. `HANDOVER.md` and `COMMITS-TODO.md` are tracked in the public repo and list open security gaps; CLAUDE.md says no reviews in the repo. Consider untracking both.
3. The `nuget` environment did not exist when this was written; it was created on 2026-11-11 (see M5). The nuget.org Trusted Publishing policy is still to do.
Deferred minors (M4):
- QA: `NamedClient_RedactsAllLoggedHeaders` (Acs and Postmark tests) is vacuous and ACS is not in the real-pipeline leak test; `PackageConstantsTests.Composer_can_be_constructed` cannot fail; public leftovers (`AppPluginsFolder`, `AliasPrefix`, `EmailSendException(string, Exception?)`, public validator and parser); docs say UMBRACO_17 is only the `SendAsync(EmailMessage)` overload but it also holds two obsolete `IEmailSender` overloads (CLAUDE.md, Directory.Build.props, design section 2); `HttpApiEmailSenderClient` reads `CurrentValue` outside its try.
- Security: strip Cf, Zl and Zp characters in `SafeFileName` and subject (RTLO); no scheduled vulnerability scan (add a cron to build.yml); unused `GHSA-6ffg-mjg7-585x` ignore in osv-scanner.toml; README should warn that Postmark Activity keeps invite and reset links; pin the SQL Server image by digest; `security-events: write` on the osv job.
- To check live in Task 11: the Forms email path, and `&#xE000;0&#xE001;` in a Forms field through Notify.

## Paused here (2026-11-05, M3)

Milestone: M3 done (Task 6b GOV.UK Notify provider, Task 7 health check and leak tests, Task 8 docs, Marketplace and release workflow). Rulings: M3 = Tasks 6b, 7 and 8 as the M2 note set out. Notify config key is `GovUkNotify` (permanent once released); no From needed for Notify (`IEmailProvider.UsesFromAddress`, `OutboundEmail.Create(fromRequired)`); attachments, Cc and Bcc fail before sending; a message reply-to is ignored with a warning (not failed, so an invite is never blocked). Cost if wrong: a rename or a behaviour flip before 1.0.
Tests: `./scripts/test.sh` passes on both lines (unit 254 on 17, 252 on 18; integration 2/2 each). `check-vulnerable` clean on both (no new package). `actionlint` clean. CI has not run.
TDD note: Task 7's `HealthCheckTests` and `EmailProviderHealthCheck` were written together and passed first time (no watched RED); `LeakTests` was proven by mutation (a deliberate address in a Postmark log line failed 6 cases).
Reviews (diff since the M2 commit): qa-reviewer no Critical (4 Important, 9 Minor); security-analyst no Critical (1 High, 1 Medium, 5 Low, 3 Info). Fixed with a failing test first: LeakTests now echo request headers, include JWT/signature sentinels, require every path to throw, read scopes, and run a real `IHttpClientFactory` pipeline at Trace (this found that the factory's structured log state holds the raw credential header, so all three clients now use `RemoveAllLoggers()`); Notify partial failure reports "N of M" for any exception; Markdown link injection into Notify (brackets neutralised, real anchors kept); `--skip-duplicate` on the NuGet push. The security High (the `nuget` environment, the Trusted Publishing environment field and a tag ruleset are never set up) is Pascal's: see REPO-SETUP step 6 and the to-do in HANDOVER at the end.
Found: `.github/workflows/publish.yml` was already modified on disk (split build and publish jobs, OIDC only in publish) when I first read it this session; I did not write that change. I reviewed it (actionlint clean, SHAs resolve) and left it in the milestone commit. Say if it should not be.
Next task: **Task 9 is Pascal's** (first full GitHub CI run: repo public, push, PR, Release checks). Then Task 10 (final whole-branch review: qa-reviewer plus security-analyst, full review) and Task 11 (exercise it for real on 17 and 18; ask Pascal for Postmark, ACS and a Notify trial-mode key, set in user-secrets, then). HANDOVER.md is written when the final milestone is done.
Deferred minors:
- NotifyProvider: `TimeoutSeconds` is per request (up to 50 x); no input-length cap before the HTML regexes; unquoted `href` links are dropped; empty subject or body gives an opaque 400; 403 hint should mention clock skew; reply-to fails open (consider `ReplyToBehaviour`); 64 KB template cap gives a misleading Warning for a huge template.
- `OutboundEmail.Create` still validates an unparseable From when `fromRequired` is false.
- `IEmailProvider`: the `<exception>` doc now sits on `UsesFromAddress` instead of `SendAsync`.
- Formatter test gaps (parentheses in URLs, uppercase tags, unterminated script); no integration test that Umbraco's collection builder constructs `EmailProviderHealthCheck` with no provider set; `Check_HasNoActions` is low value; LeakTests does not cover ACS in the real-pipeline test or `HttpApiEmailSender`.
- publish.yml: drop `setup-dotnet` from the publish job, check the nupkg names against the tag, `persist-credentials: false` on checkout.
- Carried from M2: ACS no recipient-count guard, 10 MB cap is raw bytes, no concurrency test on the ACS client cache, `Redirect_IsAFailureAndTheTokenIsNotResent` proves only that a 3xx fails, MimeKit used transitively, config binder echoes a mistyped `TimeoutSeconds`/`MaxTotalAttachmentBytes`/`DisableLinkTracking`, `EmailProviderKindParser` is public, attachment path is synchronous and buffers, never `{@}`-destructure the options or `OutboundEmail` in logs.
- To check live in Task 11: ACS and the `Expires` header; Notify trial-mode refusal text, `((subject))` in the subject, the 30-second token window, Markdown rendering of personalisation, dashboard visibility of reset links.

## Milestone commit

Check `git status` first: only `src/`, `COMMITS-TODO.md` and `HANDOVER.md` should be listed (no secrets, no `artifacts/`, no user-secrets, no review docs). No lock file changed in M4. Ignored `bin/`, `obj/` and schema files are fine.

```bash
git add -A
git commit -m "fix: final review findings

Notify body formatter no longer uses in-band link placeholders, so entity-encoded
private-use characters in visitor text cannot forge or crash a link. A malformed
Azure Communication Services connection string now fails at start-up, naming the key
and never echoing the value. Adds booted-host integration tests that the provider
replaces both the sender and the client with no Smtp:Host, and that core is untouched
when no provider is set."
git log -1 --stat | head -5
```
