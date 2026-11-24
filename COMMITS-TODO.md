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
- [ ] Task 11: live exercise (narrowed to Postmark, moved to M10)
- [x] Task 12 checkpoint: `chore: refresh docs and import provider fixtures`
- [x] Task 13 checkpoint: `refactor: provider registry`
- [x] Task 14 checkpoint: `feat: shared provider pipeline`
- [x] Task 15 checkpoint: `feat: conformance suite; Postmark on the shared pipeline` (PostmarkProviderTests kept; no case deleted, two renamed to the 64 KB cap and no base address)
- [x] Task 16 checkpoint: `feat: Notify and ACS in the conformance suite`
- [x] Task 16.5 (ruling): allowed-host check on every pipeline mapper (M6 deferred security minor), done before Mailgun
- [x] Task 17 checkpoint: `feat: SendGrid provider`
- [x] Task 18 checkpoint: `feat: Resend provider`
- [x] Task 19 checkpoint: `feat: Mailgun provider`
- [x] Task 20 checkpoint: `feat: Brevo provider`
- [x] Task 21 checkpoint: `feat: Mailjet provider`
- [x] Task 22 checkpoint: `feat: Bird provider`
- [x] Task 23 checkpoint: `feat: MailerSend provider`
- [x] Task 24 checkpoint: `feat: SMTP2GO provider`
- [x] Task 25 checkpoint: `feat: Elastic Email provider`
- [x] Task 26 checkpoint: `feat: Mandrill provider`
- [x] Task 27 checkpoint: `feat: Mailtrap provider` (also: fake server counts only a genuinely pending answer as in flight; a clock-driver race timed out Mandrill C11 once under load)
- [x] Task 28 checkpoint: `feat: Microsoft Graph token source`
- [x] Task 29 checkpoint: `feat: Microsoft Graph provider`
- [x] Task 30 checkpoint: `docs: provider guides and status table`
- [x] Task 31 checkpoint: `fix: final review findings (provider extension)`
- [x] Task 32 Step 1 (agent pre-push checks) done; Steps 2 to 4 are Pascal's (push, PR, Release checks)
- [x] Task 32: PR #8 checks and Release checks green (Pascal asked for the push, PR and workflow run in session)
- [~] Task 11: partly run 2026-11-24 (Postmark Sandbox, Umbraco 18 only; see `docs/spikes.md`); the rest parked by decision
- [ ] Task 33 (optional live checks): not asked yet
- [~] Task 34 Step 1 and 2 done (README labels match the evidence, HANDOVER.md written); Steps 3 to 4 are Pascal's (merge, tag `release/0.0-alpha.1`, `gh release create`)

## Paused here (2026-11-24, M10: guides, final review, pre-push checks)

Milestone: Tasks 30, 31 and 32 Step 1 done. README has the status table, a guide per provider and the shared rules; tags carry the provider names and no `umbraco-marketplace` tag (prerelease); CLAUDE.md gained the pipeline, registry guard and unknowns rules. `ReadmeTests` added (guide and label per provider, Notify Experimental, no "supported" beyond a label, health-check and 401 wording, tags). Docs refreshed from the workspace (identical); the path check printed nothing.
Tests: `./scripts/test.sh` passes twice in a row on both lines (unit 1251 on 17, 1249 on 18; integration 4/4 each). gitleaks clean over `src/` and the fixtures. `check-vulnerable` clean on both lines; `check-release-version.sh release/0.0-alpha.1` exits 0. **CI has not run on M6 to M10.** Nothing was run live.
Final reviews (whole branch): qa-reviewer (opus): no Critical, 3 Important, 8 Minor. security-analyst (opus): no Critical, High or Medium, 7 Low, 10 Info; signed off for an alpha, not for production. Fixed with a failing test first: README health-check claim (only Postmark, Notify and Graph check credentials); Mandrill and SMTP2GO partial acceptance now says the others were accepted; Elastic Email sends no idempotency key and is not retried after a 5xx. Also README 401 wording (Graph refreshes once).
Rulings: Elastic Email is `IdempotencyCarriage.None` (design 9.11 safe default; its two old tests replaced); a partial-acceptance error keeps the shared "rejected the email" prefix and adds a fixed hint; Brevo duplicate-after-429, Resend 409, body-read failure after a 2xx and handler filters stay deferred (speculative or rare, each needs a provider behaviour nobody has verified). Cost if wrong: a duplicate or lost invite in a rare provider failure.
Deferred minors (M10): listed in `HANDOVER.md` ("Deferred minors"). The Graph request-size limit (about 4 MB recalled, unchecked) is in `docs/spikes.md` under M10.
Next: **Pascal runs the Milestone commit below, then Task 32 Steps 2 to 4 (push, PR, Release checks).** Then Task 11 (Postmark, Pascal-gated: say which option you chose and put the token in user-secrets), optional Task 33, and Task 34 (labels check, tag). HANDOVER.md is written.

## Paused here (2026-11-24, M9: Microsoft Graph)

Milestone: Tasks 28 and 29 done: `GraphTokenSource` (one cached Entra client-credentials token, single-flight, 5 minute skew with a 60 s floor, dropped on a settings change) and the Graph provider on the shared pipeline (`MicrosoftGraph` kind and options, validation, mapper, profile, health check). Docs refreshed from the workspace (identical); the path check printed nothing. gitleaks clean over `src/`. New pipeline hooks on `IProviderMapper` (`AcquireCredentialAsync`, `RefreshesCredentialOnUnauthorized`/`ForgetCredential`, `CheckHealthAsync`), all defaulting to no-ops, so the 14 other providers are unchanged; a Debug line when a provider that ignores From was given one.
Tests: `./scripts/test.sh` passes on both lines (unit 1229 on 17, 1227 on 18; integration 4/4 each), repeated three times. No new package, no lock file change. CI has not run on this milestone. Nothing was run live. Mutation checks made tests fail: removing the token double-check, registering the token client with a plain `AddHttpClient`. Removing the "refresh once" flag makes the send loop hang rather than fail (noted by the QA review). Task 28 and 29 tests were written before the code (compile errors, then missing registration, were the watched RED); most passed on the first run after implementing.
Reviews (uncommitted M9 diff against `52a553b`): security-analyst (opus): no Critical or High, 1 Medium, 2 Low, 5 Info. qa-reviewer (opus): no Critical, 2 Important, 9 Minor. Fixed:
- Medium: a Graph 503 was retried (shared table) although Graph has no idempotency key and design 9.4 says a 5xx is never retried. Only 429 retries now (tests for 500, 503, 504; C11 not applicable to Graph).
- Important: a 401 refresh on the last attempt lifted the attempt bound (a throttled tenant would be hammered until the budget ended). Refresh only before the last attempt, and the bound checks are `>=`.
- Important: the client redirect/handler guard missed the token client, which carries the client secret. It now covers it.
- Low: a token fetched while the settings changed could be cached (the entry now carries its generation); GUIDs with surrounding whitespace passed validation (canonical form only); a malformed token was cached (RFC 6750 characters only); `%` removed from the sender address pattern.
- Minor: F11 reason corrected and a 35 MB request-cap test added; token-count assertions added (F4, the 3 MB test); the short-lifetime test now checks the cached side.
Rulings: a Graph 503 is not retried (stricter than design 9.2, per 9.4); the 401 text is the shared format with a fixed hint, not the design's bespoke sentence; F1 uses 403 as the auth signal (a 401 refreshes, so it sends twice); conformance gained an `Auxiliary` hook so the token request is answered but not counted as a send; one Debug line for any provider with `UsesFromAddress = false` (Notify too). Cost if wrong: small text changes before the first alpha. Unknowns are in `docs/spikes.md` under M9.
Deferred minors (M9):
- `Invalidate()` is not compare-and-clear: many sends getting a 401 at once can make a few extra token requests.
- Concurrent-send test uses a fixed 100 ms delay (can only false-pass); no concurrent `SendAsync` test at provider level.
- `Unauthorized_RefreshesOnceThenFails` would hang, not fail, if the flag were removed; no xUnit timeouts on the Graph pipeline tests.
- The Debug From line fires on every Graph or Notify send where a From is configured (design says "differs"), has no Notify test, and a malformed configured From still fails a Graph send.
- No leak-sweep path where the token endpoint fails (covered only in `GraphTokenSourceTests`); a hanging token endpoint has no test.
- README line to add in M10: Graph returns no message id and keeps no copy, so misuse leaves only Exchange message trace.
- One C11 failure on line 17 seen by the QA review mid-session was the 503 change landing before C11 was marked not applicable for Graph; three full runs since are clean.
- Carried from M7 and M8 and still open (see those notes).
Next task: **Task 30 (README provider guides, status table and tags), the start of M10** (Sonnet session; reviewers on opus). Task 11 (Postmark live) is Pascal-gated and also sits in M10.

## Paused here (2026-11-24, M8: wave B)

Milestone: Tasks 22 to 27 done: Bird, MailerSend, SMTP2GO, Elastic Email, Mandrill, Mailtrap, each on the shared pipeline with a conformance profile, quirk tests and a registry entry. Docs refreshed from the workspace (identical); the path check printed nothing. gitleaks clean over `src/`. New: `IProviderMapper.StartupWarnings` and one `ProviderStartupWarnings` handler (Bird and SMTP2GO key-prefix hints, fixed text, never the key).
Tests: `./scripts/test.sh` passes on both lines (unit 1095 on 17, 1097 on 18; integration 4/4 each), three full runs in a row after the review fixes. No new package, no lock file change. CI has not run on this milestone. Nothing was run live. Every new test was written first and failed (missing types or profile) before the code.
Review (diff against `82421f4`): qa-reviewer (sonnet): no Critical, 2 Important, 5 Minor. Fixed with a failing test first: SMTP2GO accepted any parseable 2xx with no failure marker (now needs an id or a success count, else fails closed); Bird sent `from` as a display-name string where its own example uses `{email, name}` (now the object). All five departures judged acceptable.
Found and fixed on the way: `FakeProviderServer` counted an instant responder as in flight, so the clock driver could advance past the send budget under load (Mandrill C11 timed out once). It now counts only a still-pending answer (`FakeProviderServerTests`).
Rulings: error text for Bird 421, MailerSend 202 and Mandrill rejections uses the shared format with a fixed hint, not the plan's bespoke sentences; MailerSend `x-send-paused` is accepted with a Warning with or without an id; Mandrill omits `preserve_recipients`; Bird has no quota stop (docs ambiguous); the unverified-sender signal for Elastic Email is not applicable (undocumented). Unknowns are in `docs/spikes.md` under M8. Cost if wrong: small text changes before the first alpha.
Deferred minors (M8):
- MailerSend: a paused 202 with no id is logged as "accepted ... message id none"; the empty-body vs readable-body 202 test cannot tell the two paths apart.
- Mandrill test `RequestTypes_ToStringNeverPrintTheKey` has two vacuous lines (`HttpContent.ToString()` prints no body); the record-level lines are real.
- `FakeProviderServerTests.HangingResponder_IsCountedUntilItEnds` spins without a bound.
- Elastic Email retries a 500 on the strength of an `Idempotency-Key` that is absent from its older reference (live check needed); Mailtrap accepts `{}` or a non-boolean `success` as sent (within design 9.2).
- Carried from M7 and still open: secret Trim check, registry-driven `InvalidOptions_NameKeysNeverValues`, echo-body allow-list token leak case, Mailjet 50-attachment test, shared empty-body pre-check, Resend and Mailgun From quoting, AllowedHosts port, address de-duplication.
Next task: **Task 28 (Microsoft Graph token source), the start of M9** (Opus session; qa-reviewer and security-analyst on opus).

## Paused here (2026-11-23, M7: wave A)

Milestone: Tasks 17 to 21 done: SendGrid, Resend, Mailgun, Brevo, Mailjet, each on the shared pipeline with a conformance profile, quirk tests and a registry entry. Docs refreshed from the workspace (design and plan were already identical); the path check printed nothing. gitleaks clean over `src/`.
Tests: `./scripts/test.sh` passes on both lines (unit 747 on 17, 745 on 18; integration 4/4 each). Mutation checks (SendGrid `reply_to_list`, Brevo retry rule, Mailjet fail-closed) each made tests fail. No new package, no lock file change. CI has not run on this milestone. Nothing was run live.
Review (diff against `a5478cc`): qa-reviewer (sonnet): no Critical, no Important, 9 Minor. Fixed: Brevo's duplicate-on-retry rule no longer depends on HTTP 400 (any 4xx; a wrong guess would re-send an invite); Mailjet no longer accepts an all-success body on a non-2xx status. These two tests were written alongside the fix, so there was no watched RED.
Rulings: added `ProviderCapabilities.AllowedHosts` (the pipeline refuses any other host; the M6 deferred security minor, done before Mailgun); `ReadOutcome` gained an attempt-aware overload (Brevo only); LeakTests now takes options from each profile, adds every profile secret as a sentinel, prints every options class and drives a `SteppingClock` (the old file configured only the three built providers and hung on idempotent retries); multipart shape match by part name; F11 `BinaryAttachments`; two old tests used "Mailgun" as an unknown provider name, now "Mailchimp". Unknowns are in `docs/spikes.md` under M7. Cost if wrong: small text changes before the first alpha.
Deferred minors (M7):
- Secret validation lets leading or trailing spaces through (Mailgun and Mailjet put them inside Basic credentials); add a `Trim` check.
- `InvalidOptions_NameKeysNeverValues` covers only the three built providers; make it registry-driven.
- Echo bodies never carry an allow-list token (Resend `name`, Brevo `code`, Mailjet `ErrorCode` could carry a key-shaped string through `SafeCode`); add a leak case and tighten per-provider code shapes.
- Mailjet's 50-attachment cap has no test.
- Only SendGrid refuses an empty body before sending; move the check into the shared pre-checks and skip empty Mailgun parts.
- Resend and Mailgun send `From` unquoted: a name such as `Acme Inc.` may need quoting (unverified).
- AllowedHosts does not compare the port; no de-duplication of addresses across To, Cc and Bcc; how .NET encodes a quote or non-ASCII character in a Mailgun attachment filename is untested against a real server.
Next task: **Task 22 (wave B: Bird), the start of M8.**

## Paused here (2026-11-16, M6: shared pipeline and conformance suite)

Milestone: Tasks 12 to 16 done. Workspace design and plan copied in verbatim; the path check printed nothing. 13 provider fixture folders imported and scrubbed (gitleaks clean). Provider registry; shared pipeline (`Providers/Pipeline/`: pre-checks, size cap, the design 9.2 retry table, one budget across attempts, 64 KB capped reads, one log line); conformance suite (C1 to C12, F1 to F15) with Postmark, Notify and ACS profiles; Postmark and Notify on the pipeline; ACS runs the shared pre-checks (50-recipient cap) and stays off pipeline retries; registry guard; `LeakTests` driven by the registry.
Tests: `./scripts/test.sh` passes on both lines (unit 455 on 17, 453 on 18; integration 4/4 each). No new package. gitleaks clean over `src/`. CI has not run on this milestone.
Reviews (uncommitted M6 diff against `405489c`): security-analyst (opus) no Critical, 1 High, 1 Medium, 3 Low, 2 Info; qa-reviewer (opus) no Critical, 1 Important, 5 Minor. Fixed with a failing test first:
- High: a site's `ConfigureHttpClientDefaults` (Aspire resilience handler, Polly) would wrap provider clients and resend POSTs (duplicate invites). Every provider client now clears host handlers; tested for every provider and both registration orders.
- Medium: a mapper could mark a possibly accepted send retryable. The pipeline now enforces the table; a mapper can only narrow it.
- Important: the fake-clock test rigs advanced on wall time (ACS F5 flaked about 1 in 15). A new `SteppingClock` advances only when a send is waiting on a timer; 10 of 10 repeat runs clean.
Rulings (summary): validation in per-provider classes; `IProviderMapper` gained `ClientName`, `UsesFromAddress` and `ReadHealth`; capabilities gained `ReplyToHint` and `ErrorCodeLabel`; `FromAddress`/`FromName` are computed from the normalised From; the Postmark and Notify clients have no base address (absolute URI per send); read cap 64 KB; four existing test cases adjusted (the 64 KB cap, no base address, and two Notify 429s moved from `RateLimitError` to the daily-limit class `TooManyRequestsError`, because a plain 429 now retries); ACS gets the shared success log line, a 401 hint and the shared 429 text; extra not-applicable entries for ACS (SDK path), each with a reason. Cost if wrong: small text or log-format changes before the first alpha.
Q4 (shared cap of 50, no general fan-out) still stands on its default.
Deferred minors (M6):
- Security: `SafeId` admits address-shaped ids into the success log; ACS `SafeCode` and Notify `ErrorClass` use `$`, not `\z`; **no per-mapper allowed-host check on request URIs (do this before Bird, Mailgun or Graph build hosts from options)**; back-off computed twice on the idempotent connection-failure path; `AcceptedOutcome.Warning` is free text (make it constant before MailerSend).
- QA: F15 never reaches the server for Notify; the Notify Reply-To warning is logged once per recipient; `Unreadable` on a non-2xx skips the retry table; no Notify test that `RateLimitError` retries; no test sweeps a waited retry whose response echoes secrets.
Next task: **Task 17 (wave A: SendGrid), the start of M7.** Consider adding the allowed-host check first, since Mailgun is in that wave.

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

`main` has a ruleset, so this goes on a branch and a squash-merged PR. `git status` should list only `CLAUDE.md`, `COMMITS-TODO.md`, `HANDOVER.md`, `README.md`, `docs/spikes.md` and files under `src/` (no secrets, no `artifacts/`, no review docs). No lock file changed in M10. `COMMITS-TODO.md` and `HANDOVER.md` are tracked in the public repo (undecided since M4): drop them from the `git add` if you would rather keep them private.

```bash
git switch -c feat/m10-guides-and-final-review
git status --short
git add -A
git commit -m "docs: provider guides, status table and final review fixes

Adds the README status table and a guide per provider, the shared retry and
size rules, and Graph's RBAC warning. The prerelease package drops the
Marketplace tag and gains the provider tags. ReadmeTests guard the labels.

Final review fixes: the health-check text no longer claims every provider
checks credentials; Mandrill and SMTP2GO say the other recipients were
accepted when a send partly fails; Elastic Email sends no idempotency key
and is not retried after a 5xx until a live check shows the key works."
git log -1 --stat | head -5
```


