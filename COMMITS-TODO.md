# Commits to make

Pascal commits once per milestone. Plan checkpoints are logged here as a record.

## Checkpoint log

- [x] Task 1 checkpoint: `chore: scaffold v18 line, demo site and CI` (both lines, via the `UmbracoMajor` switch)
- [x] Task 2 checkpoint: `feat: options and provider-gated composer`
- [x] Task 3 checkpoint: `feat: normalise EmailMessage to OutboundEmail`
- [x] Task 4 checkpoint: `feat: IEmailSender and client that bypass the SMTP gate`
- [x] Task 5 checkpoint: `feat: Postmark provider`
- [x] Task 6 checkpoint: `feat: Azure Communication Services provider`

## Paused here (2026-11-05, M2)

Milestone: M2 done (Tasks 5 and 6: Postmark and Azure Communication Services providers). The plan defines no milestones, so I ruled M2 = the two planned providers. Ruling: GOV.UK Notify is not in plan.md yet, so it opens M3 as new Task 6b, with the health check, leak tests and docs. Cost if wrong: one reshuffle of M3.
Tests: `./scripts/test.sh` passes on both lines (unit 153 on 17, 151 on 18; integration 2/2 each). `check-vulnerable` is clean. New package: `Azure.Communication.Email` 1.1.0; lock files regenerated for both lines (8 files). CI has not run.
Reviews (diff since the M1 commit): qa-reviewer found no Critical (3 Important, 4 Minor); security-analyst found no Critical or High (2 Medium, 5 Low, 5 Info). Fixed with a failing test first: `TimeoutSeconds` capped 1 to 300; providers wrap any non-`EmailSendException` as type name only (Postmark and ACS); ACS status 0 reported as "could not be reached"; SDK network timeout raised; token control/non-ASCII rejected at start-up; factory header logging redacted on both clients; one strict ACS connection-string parser with an https and `*.communication.azure.*` host allow-list, client built from parsed parts; `PostmarkHeader.ToString`; ACS caller-cancellation test. Carried in from M1 and done: `TimeoutSeconds` applied in providers, `IEmailProvider` registered per provider in the composer, container test resolves `IEmailSender` for both providers, Postmark address parsing re-checked (spikes).
Ruling: no config override for the ACS host allow-list (config keys are permanent). Cost if wrong: a sovereign-cloud user waits for a patch.
Next task: **Task 6b (new): GOV.UK Notify provider**, then Task 7 (health check and leak tests; extend the leak theory to all three providers), Task 8 (README and docs). Read `~/Documents/Projects/Umbraco Packages/15-http-email-sender/scope.md` section "Added provider" first, and add Task 6b to `docs/plan.md`. Ask Pascal for a Notify trial-mode key only at Task 11.
Deferred minors:
- ACS has no recipient-count guard (Postmark has 50). The 10 MB attachment cap is raw bytes; base64 makes it about 13.3 MB on the wire, so say so in the README.
- README must say: a `Provider` change needs a restart; a host-level resilience handler (`ConfigureHttpClientDefaults`) would retry POSTs and duplicate emails; the ACS endpoint must be a `*.communication.azure.*` host.
- Test gaps: no concurrency test for the ACS client cache lock; no check that the health checks honour `TimeoutSeconds`; log-output leak assertions on failure paths come with Task 7's leak theory.
- `Redirect_IsAFailureAndTheTokenIsNotResent` proves only that a 3xx is a failure; the no-redirect guarantee is `NamedClient_HasPostmarkBaseAddressAndNoRedirects`.
- ACS operation-id idempotency would allow safe retries later; `IsTelemetryEnabled` left on; the ACS response size is unbounded (bounded by the host allow-list and the timeout).
- To check live in Task 11: ACS and the `Expires` header.
- From M1, still open: MimeKit used transitively; the config binder echoes a mistyped value for `TimeoutSeconds`, `MaxTotalAttachmentBytes` and `DisableLinkTracking`; `EmailProviderKindParser` is public; the attachment path is synchronous and buffers; never `{@}`-destructure the options or `OutboundEmail` in logs.

## Milestone commit

Check `git status` first: only `src/`, `docs/spikes.md`, the lock files and this file should be listed (no secrets, no `artifacts/`, no user-secrets, no review docs). Ignored `bin/`, `obj/` and schema files are fine.

```bash
git add -A
git commit -m "feat: Postmark and Azure Communication Services providers

Adds PostmarkProvider (POST /email, per-message link tracking off, 50-recipient limit)
and AcsProvider (Azure.Communication.Email 1.1.0, WaitUntil.Started, retries off,
strict connection-string parsing). Both enforce EmailSender:TimeoutSeconds (1 to 300),
follow no redirects, redact logged headers and fail with status-only EmailSendException.
The composer registers the provider chosen by EmailSender:Provider."
git log -1 --stat | head -5
```
