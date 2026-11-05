# Commits to make

Pascal commits once per milestone. Plan checkpoints are logged here as a record.

## Checkpoint log

- [x] Task 1 checkpoint: `chore: scaffold v18 line, demo site and CI` (both lines, via the `UmbracoMajor` switch)
- [x] Task 2 checkpoint: `feat: options and provider-gated composer`
- [x] Task 3 checkpoint: `feat: normalise EmailMessage to OutboundEmail`
- [x] Task 4 checkpoint: `feat: IEmailSender and client that bypass the SMTP gate`

## Paused here (2026-11-05)

Milestone: M1 done (Tasks 2 to 4: options, validator, composer, `OutboundEmail` mapping, `HttpApiEmailSender`, client). The plan defines no milestones, so M1 = Tasks 2-4 (the provider-independent core). `./scripts/test.sh` passes on both lines (unit 87 on 18, 89 on 17; integration 2/2 each). `check-vulnerable` is clean on the last restore. CI has not run.
Reviews (diff since the M0 commit): qa-reviewer found no Critical; security-analyst found no Critical (one High, fixed). Fixed with a failing test first: recipient and From normalisation (bare addresses, CR/LF rejected), subject and file-name sanitising, non-`EmailSendException` errors wrapped with no inner exception or message text, attachment cap tests, composer wiring tests, notification mapping tests, strict provider parsing, `MessageStream` and cap-range validation, notification From fallback, seekable attachment rewind.
Next task: **Task 5** (Postmark provider). Carry into Task 5:
- apply `TimeoutSeconds` inside the provider (the client passes `CancellationToken.None`);
- register `IEmailProvider` in the composer (without it `IEmailSender` cannot resolve, so do not pack or tag before this);
- add a container test that resolves `IEmailSender` for each provider;
- throw `EmailSendException` with no inner exception and no provider free text;
- re-check how Postmark parses addresses.
Note: plan.md has no GOV.UK Notify task yet. Add one before Task 11.
Deferred minors: MimeKit is used transitively (no explicit PackageReference); the config binder echoes a mistyped value for `TimeoutSeconds`, `MaxTotalAttachmentBytes` and `DisableLinkTracking` (only `Provider` is pre-parsed); `EmailProviderKindParser` is public; the attachment path is synchronous and buffers (about 2.7x the cap at peak); never `{@}`-destructure the options or `OutboundEmail` in logs; the README must say a `Provider` change needs a restart.

## Milestone commit

Check `git status` first: only `src/` changes and the docs below should be listed (no secrets, no `artifacts/`, no user-secrets, no review docs).

```bash
git add -A
git commit -m "feat: options, mapping and sender that bypass the SMTP gate

Adds EmailSender options with start-up validation and redacted secrets, a composer that
replaces IEmailSender and IEmailSenderClient only when EmailSender:Provider is set,
OutboundEmail mapping (From fallback, normalised recipients, attachment cap, Expires
header), HttpApiEmailSender with core's notification-first order, and the client that
hands one OutboundEmail to a provider. Provider implementations follow."
git log -1 --stat | head -5
```
