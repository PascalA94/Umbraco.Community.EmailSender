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

Check `git status` first: only `src/`, `docs/`, `README.md`, `CLAUDE.md`, `umbraco-marketplace.json`, `.github/` and this file should be listed (no secrets, no `artifacts/`, no user-secrets, no review docs). No lock file changed in M3. Ignored `bin/`, `obj/` and schema files are fine.

```bash
git add -A
git commit -m "feat: GOV.UK Notify provider, health check and docs

Adds NotifyProvider (hand-signed HS256 token, one request per recipient, HTML reduced
to text with Markdown links neutralised, no attachments, Cc or Bcc, no From needed),
the Email provider health check, and leak tests over every provider and failure path,
including a real HttpClientFactory pipeline run at Trace. The named HTTP clients drop
the factory's own logging because its structured state carried the credential header.
README, CLAUDE.md, Marketplace tags and the release workflow (split build and publish
jobs, --skip-duplicate, environment setup in REPO-SETUP)."
git log -1 --stat | head -5
```
