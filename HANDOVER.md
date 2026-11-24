# Handover

State on 2026-11-24, end of M10 (agent part). **CI has not run on M6 to M10.** Nothing has been run against a live provider.

## What was built

- Package `Umbraco.Community.EmailSender`: sends all Umbraco email through an HTTP API instead of SMTP. One branch builds Umbraco 17 (17.8.0 or later) and 18 (18.3.0 or later) through `UmbracoMajor`.
- 15 providers: Postmark, Azure Communication Services (SDK), GOV.UK Notify, SendGrid, Resend, Mailgun, Brevo, Mailjet, Bird, MailerSend, SMTP2GO, Elastic Email, Mandrill, Mailtrap, Microsoft Graph.
- One shared pipeline (`Providers/Pipeline/`: pre-checks, size cap, one time budget, the retry table, capped reads, one log line), one provider registry, one conformance suite (C1 to C12, F1 to F15) with a profile per provider, and a leak sweep driven by the registry.
- README: status table, one guide per provider, shared rules, Graph's RBAC warning first. `PackageTags` carry the provider names and no `umbraco-marketplace` tag while the version is a prerelease.
- No new package since the ACS SDK. No lock file changed after M5.

## Gates and tests

- `./scripts/test.sh`, run twice in a row after the last change: 17 unit 1251 and integration 4/4; 18 unit 1249 and integration 4/4; `Failed: 0` each time.
- `gitleaks dir src --redact` and `gitleaks dir src/Umbraco.Community.EmailSender.Tests/Fixtures --redact`: no leaks.
- Path grep over the repo (excluding `.git`, lock files, build output): no hits. `docs/` holds only `design.md`, `plan.md`, `spikes.md`.
- `bash build/check-vulnerable.sh` after a locked restore of each line: no vulnerable packages. `bash build/check-release-version.sh release/0.0-alpha.1`: exit 0.
- Mutation checks and watched-RED tests are noted per milestone in `COMMITS-TODO.md`.

## Reviews and fixes (summary)

- M6 (opus, qa and security): a site-wide HTTP resilience handler could resend POSTs (fixed for every provider); a mapper could widen the retry table (the pipeline now enforces it); test-clock flakes (fixed).
- M7, M8 (sonnet, qa): Brevo duplicate-on-retry rule, Mailjet fail-closed, SMTP2GO fail-closed, Bird `from` object: fixed.
- M9 (opus, qa and security): Graph 503 no longer retried; the 401 refresh cannot lift the attempt bound; the token client has the redirect guard; token and id validation tightened: fixed.
- Final whole-branch review (M10, opus, qa and security): no Critical, High or Medium. Fixed with a failing test first:
  - README health-check text claimed most providers check credentials; only Postmark, Notify and Graph do, the rest report Info.
  - Mandrill and SMTP2GO reported a partly accepted send as a plain rejection, which invites a duplicate resend; the error now says the others were accepted.
  - Elastic Email retried 5xx on an `Idempotency-Key` never seen to work; no key is sent and it is not retried after a 5xx (design 9.11).
  - README now says Microsoft Graph repeats a 401 once.

## Labels and evidence

- Postmark: "Built from the provider's docs and checked against recorded examples". It becomes "Verified live" only after Task 11 records a real send in `docs/spikes.md`.
- ACS: Unverified (retires 30 Sep 2028). GOV.UK Notify: Unverified, Experimental.
- The other 12: "Built from the provider's docs and checked against recorded examples". No provider is "Verified live". The README never says "supported" beyond a label.

## Unverified

- Everything on GitHub: CI has not run on M6 to M10.
- Every provider live (Task 11 and Task 33 not run).
- Elastic Email's `Idempotency-Key`; Bird and `Expires`; Graph's request size limit (a reviewer recalled about 4 MB; not checked); Notify trial-mode text; ACS and `Expires`. Details: `docs/spikes.md`.
- The README screenshot shows a passing Postmark health check. Its origin is not recorded in `docs/spikes.md`: caption it as illustrative or record it.

## Deferred minors (still open)

Full per-milestone lists are in `COMMITS-TODO.md`. The ones from the final review:

- Brevo `duplicate_parameter` is read as accepted even after a 429 or 503 first attempt (only if Brevo records the key anyway).
- Resend 409 `concurrent_idempotent_requests` on a retry is a plain rejection (probably accepted).
- A body read that fails after a 2xx is reported as a timeout or failure, not as accepted for providers that do not fail closed.
- Handler filters (`IHttpMessageHandlerBuilderFilter`) from other libraries are not cleared.
- RTLO and other format characters survive in subjects and file names; Notify per-recipient budget; config binder echoes a mistyped non-secret value; ACS `SafeCode` uses `$`; `SafeId` admits `@`; health check skips the allowed-host check.
- Echo-code leak case per provider; secret `Trim` check; `Invalidate()` is not compare-and-clear; a fixed 100 ms delay in the Graph concurrency test.
- CI: no scheduled vulnerability scan; an unused osv ignore; the SQL Server image is not digest-pinned; `publish.yml` checkout keeps credentials.
- README note to keep: Graph returns no message id and keeps no copy, so a misuse leaves only Exchange message trace.

## Pascal's to-do list

1. Run the "Milestone commit" block at the end of `COMMITS-TODO.md` (branch, commit, PR). Do not commit `HANDOVER.md` or `COMMITS-TODO.md` to the public repo unless you decide to keep them tracked (undecided since M4; they list open gaps).
2. Task 32: push the branch, open the PR, expect every required check green in about 5 minutes. Run **Release checks** by hand on the branch. Start a session to fix any failure under the owning task's tests.
3. Task 11 (Postmark live check, gated on step 2): tell the next session which option you chose (Sandbox server, live Developer plan, or `POSTMARK_API_TEST`) and put the token in the demo site's user-secrets. Never paste it in chat.
4. Task 33 (optional): name which of Resend, Bird, Brevo, Mailjet, Mailtrap and ACS you want to run live. Each skipped provider keeps its label.
5. Task 34: README labels vs `docs/spikes.md`, then Release checks, squash-merge, tag `release/0.0-alpha.1`, `gh release create --prerelease --generate-notes`. No Marketplace listing for the alpha.
6. Before the first tag: the nuget.org Trusted Publishing policy (repository, `publish.yml`, environment `nuget`); dismiss the CodeQL `cs/web/missing-token-validation` alert as a false positive; check the icon and screenshots on nuget.org afterwards.
7. Before stable: put the `umbraco-marketplace` tag back, work through the deferred minors, and decide on label upgrades.
