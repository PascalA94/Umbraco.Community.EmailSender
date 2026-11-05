# Design: HTTP-API email sender

| | |
|---|---|
| Package | `Umbraco.Community.EmailSender` |
| Owner | Pascal Meyer (`PascalA94`) |
| Size | S, about 5-8 h agent time (4-6 h plus 1-2 h for the second line) |
| Lines | **Both from the start** (Pascal, 2026-11-04: client sites are on v17 LTS): `17.x` on CMS `[17.8.0, 18.0.0)` and `18.x` on `[18.3.0, 19.0.0)`, one codebase, both packed from one commit |
| Status | Draft design, 2026-11-04. Nothing built |
| Inputs | `scope.md`, `../README.md` (floors, CI and GitHub rules, Build approach), `~/.claude/references/umbraco-pitfalls.md`, `../01-archived-users/` (house style) |

Tags: **[verified]** read this session at the linked source (Umbraco-CMS `release-18.3.0` unless stated); **[unverified]** recalled or inferred, check while building; **[DATED]** true on 2026-11-04 and likely to move.

---

## 0. What was verified (2026-11-04)

All [verified], read from source with `gh api` at tag `release-18.3.0` (commit `caef633`), with each row re-checked at `release-17.8.0` unless the row says otherwise. On 17.8.0, the `EmailSender` SMTP gate (L67, L92), the `IEmailSender` factory registration and the `EmailMessage` shape are the same. The only API difference is the extra obsolete overload in row 2.

| Fact | Source |
|---|---|
| `IEmailSenderClient` has one member on 18: `Task SendAsync(EmailMessage message, TimeSpan? expires)` | [IEmailSenderClient.cs](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/Mail/Interfaces/IEmailSenderClient.cs) |
| On 17.8.0 it also has the obsolete `SendAsync(EmailMessage)`, and the two-argument overload has a default body that calls it | [17.8.0 IEmailSenderClient.cs](https://github.com/umbraco/Umbraco-CMS/blob/release-17.8.0/src/Umbraco.Infrastructure/Mail/Interfaces/IEmailSenderClient.cs) |
| **`EmailSender` only calls the client when `Smtp:Host` or `Smtp:PickupDirectoryLocation` is set.** Otherwise it logs at Debug and returns. Same on 17.8.0 (line 92) | [EmailSender.cs](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/Mail/EmailSender.cs), `SendAsyncInternal` |
| `CanSendRequiredEmail()` = SMTP host, or pickup folder, or a `SendEmailNotification` handler. Invites (`CanSendInvites`) and password reset (`CanSend`) depend on it | same file; [EmailUserInviteSender.cs L95](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/Security/EmailUserInviteSender.cs), [EmailUserForgotPasswordSender.cs L90](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/Security/EmailUserForgotPasswordSender.cs) |
| Invites and password reset pass `enableNotification: true`; content notifications ([NotificationService.cs L582](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Core/Services/NotificationService.cs)) and health-check emails ([EmailNotificationMethod.cs L93](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Core/HealthChecks/NotificationMethods/EmailNotificationMethod.cs)) pass `false` | as linked |
| Core registers `IEmailSenderClient` as transient and `IEmailSender` as a **singleton** built by a factory that captures the client | [UmbracoBuilder.CoreServices.cs L194-204](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/DependencyInjection/UmbracoBuilder.CoreServices.cs) |
| `EmailMessage`: `From`, `To[]`, `Cc[]`, `Bcc[]`, `ReplyTo[]`, `Subject`, `Body`, `IsBodyHtml`, `Attachments` (`Stream` + `FileName`). An empty `From` falls back to `Smtp:From` | [EmailMessage.cs](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Core/Models/Email/EmailMessage.cs), [EmailMessageExtensions.cs L18-20](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Infrastructure/Extensions/EmailMessageExtensions.cs) |
| Core's `SmtpCheck` health check tests an SMTP connection, so it will report a failure on a site with no SMTP host | [SmtpCheck.cs](https://github.com/umbraco/Umbraco-CMS/blob/release-18.3.0/src/Umbraco.Core/HealthChecks/Checks/Services/SmtpCheck.cs) |

**What this changes from the scope.** The scope says "replace `IEmailSenderClient`". That alone does nothing on a site with no SMTP host, because `EmailSender` returns before it reaches the client, and the backoffice hides invites. So the package replaces **both** `IEmailSenderClient` and `IEmailSender` (4.1).

---

## 1. Intent and goals

Send every core email through an HTTP email API instead of SMTP. Pick the provider in appsettings.

**Goals**
- Two providers in 1.0: **Azure Communication Services (ACS)** and **Postmark** (Q1).
- Every core email works: invites, password reset, content notifications, health-check reports, plus anything else that calls `IEmailSender` (Forms "send email" is expected to, [unverified], checked in the last plan task).
- HTML and plain text, From, Reply-To, Cc, Bcc and attachments.
- Optional: turn off link and click tracking, so mail scanners and trackers don't consume invite links (forum 7423).
- A health check: "Email provider configured and reachable".

**Non-goals (1.0)**
- Resend and SendGrid. Each is +1-2 h later, behind the same `IEmailProvider` seam.
- Templates, queues, retries with back-off, bounce webhooks.
- Member-facing or marketing email.

**Anti-goals**
- Never log an API key, a recipient address or a subject line. Subjects can carry names ("Sam invited you").
- Never change behaviour when `Provider` is not set. Core SMTP stays exactly as it is.

---

## 2. Constraints

- **Both lines from the start** (Pascal, 2026-11-04), using the Teams and Jira pattern:
  - `eng/lines.txt` = `17` and `18`.
  - `Directory.Build.props` has `UmbracoMajor` (`17` or `18`, default `18`). It sets `UMBRACO_17` or `UMBRACO_18`, the range (`[17.8.0, 18.0.0)` or `[18.3.0, 19.0.0)`) and `NuGetLockFilePath` = `packages.$(UmbracoMajor).lock.json`.
  - Package version is `$(UmbracoMajor).$(PackageMinor).$(PackagePatch)`. One `release/<minor>.<patch>[-suffix]` tag packs `17.x` and `18.x` from the same commit.
  - CI runs floor and latest legs **per line** (four legs).
- The only v17 code difference is the obsolete one-argument `SendAsync(EmailMessage)` [verified], added under `#if UMBRACO_17`. Everything else is shared.
- `net10.0`, plain class library: `Umbraco.Cms.Infrastructure` and `Umbraco.Cms.Core`. No client code, no controllers, no Razor.
- Pitfalls: P1 and P2 (DI only, no statics; the provider is a singleton over `IHttpClientFactory` and `IOptionsMonitor`, because core's singleton `IEmailSender` captures it); P7 (no network calls at start-up). S1, S3 and S5 don't apply.
- Portfolio CI rules (README "CI and GitHub rules") win over anything here.

---

## 3. MVP spec

### 3.1 Config keys

```json
"EmailSender": {
  "Provider": "AzureCommunicationServices",   // or "Postmark"; unset = package does nothing
  "From": null,                                // else Umbraco:CMS:Global:Smtp:From
  "DisableLinkTracking": true,
  "TimeoutSeconds": 30,
  "MaxTotalAttachmentBytes": 10485760,
  "AzureCommunicationServices": { "ConnectionString": "<secret>" },
  "Postmark": { "ServerToken": "<secret>", "MessageStream": "outbound" }
}
```

- Secrets come from env vars or a secret store in practice (`EmailSender__Postmark__ServerToken`). The README shows the env-var form first and never shows a real-looking value.
- An unknown `Provider` value is a start-up error naming the allowed values. A known provider with a missing secret is also a start-up error, naming the **key**, never the value (`ValidateOnStart`).

### 3.2 Behaviour

- `Provider` unset: no registrations change.
- `Provider` set: `HttpApiEmailSender` replaces `IEmailSender`, and `HttpApiEmailSenderClient` replaces `IEmailSenderClient`.
  - `CanSendRequiredEmail()` returns true, so invites and password reset show in the backoffice.
  - If `enableNotification` is true, it publishes `SendEmailNotification` first and stops if a handler sets `IsHandled`. This keeps core's contract.
  - It then calls the client. It ignores `Smtp:Host` and `PickupDirectoryLocation`, and logs one start-up warning if either is also set (Q2).
- The `From` order is: message `From`, then `EmailSender:From`, then `Smtp:From`. If none parses, the email fails with "No From address. Set EmailSender:From or Umbraco:CMS:Global:Smtp:From."
- `expires` is sent as an `Expires` header (RFC 1123), as core's SMTP client does.
- Attachments are read into base64. A total over `MaxTotalAttachmentBytes` fails before anything is sent.
- `DisableLinkTracking` behaves differently per provider:
  - Postmark: sets `TrackLinks: "None"` and `TrackOpens: false` on each message.
  - ACS: has no per-message switch. Tracking is a domain setting [unverified], so the README says to turn off "user engagement tracking" on the ACS domain, and the health check warns if it can't confirm the setting.
- On failure, it throws `EmailSendException` with a message like "Postmark rejected the email: HTTP 422, ErrorCode 300." The exception carries no recipient, no key, no request body and no `InnerException` holding the request. This matches core, where a failed SMTP send also throws.

### 3.3 Health check

`[HealthCheck("<new guid>", "Email provider", Group = "Services")]`:
- **Postmark:** `GET https://api.postmarkapp.com/server` with the token. 200 is Success. 401 is Error: "Postmark rejected the server token."
- **ACS:** checks that the connection string parses and the endpoint answers TLS. It can't prove that sending works without sending, which the README says.
- The README says core's "SMTP Settings" check will now fail and can be ignored [verified that it tests SMTP].

### 3.4 Acceptance criteria

- **Given** no `EmailSender:Provider`, **when** the site starts, **then** `IEmailSender` resolves to core's `EmailSender` **and** `IEmailSenderClient` to `BasicSmtpEmailSenderClient`.
- **Given** `Provider: Postmark` and no `Smtp:Host`, **when** an admin opens Users, **then** "Invite user" is offered **and** sending the invite makes one POST to `/email`.
- **Given** a content notification email (`enableNotification: false`), **when** it is sent, **then** it reaches the provider.
- **Given** a `SendEmailNotification` handler that sets `IsHandled`, **when** an invite is sent, **then** no provider request is made.
- **Given** `DisableLinkTracking: true` on Postmark, **when** any email is sent, **then** the body has `"TrackLinks":"None"`.
- **Given** a 12 MB attachment, **when** sending, **then** it fails with the size message **and** no request is sent.
- **Given** the provider returns 401 or 500, **when** sending, **then** `EmailSendException` is thrown **and** no captured log line, exception message or `ToString()` contains the token, any recipient address or the subject.
- **Given** `Provider: Postmark` and no `ServerToken`, **when** the site starts, **then** it fails naming `EmailSender:Postmark:ServerToken`.

---

## 4. Technical design

### 4.1 Why replace `IEmailSender` too

| Option | Verdict |
|---|---|
| Replace only `IEmailSenderClient` (the scope's wording; Jason Elkin's forum advice) | Fails on sites with no SMTP host: `EmailSender` returns first, and invites are hidden [verified] |
| ... plus a dummy `Smtp:Host` | Rejected. It is a lie in config that someone will "fix", and core's `SmtpCheck` would try to connect to it |
| A `SendEmailNotification` handler that sends and sets `IsHandled` | Rejected. Content notifications and health checks pass `enableNotification: false`, so they would never send [verified] |
| **Our own `IEmailSender` (about 40 lines, keeps core's notification-first order) plus our `IEmailSenderClient`** | **Chosen.** Small, covers every path, and code that resolves the client directly still gets ours |

Revisit this if core drops the SMTP gate in `EmailSender`. Then the package becomes client-only, which is what the scope first described.

### 4.2 Layout

```
src/Umbraco.Community.EmailSender/
  EmailSenderComposer.cs          // AddUnique both services only when Provider is set
  Configuration/EmailSenderOptions.cs (+ validator; secrets [JsonIgnore], ToString redacted)
  HttpApiEmailSender.cs           // IEmailSender
  HttpApiEmailSenderClient.cs     // IEmailSenderClient -> IEmailProvider
  Providers/IEmailProvider.cs     // Task SendAsync(OutboundEmail, CancellationToken)
  Providers/OutboundEmail.cs      // normalised: from, to/cc/bcc/replyTo, subject, html?, text?, attachments, headers
  Providers/Postmark/PostmarkProvider.cs
  Providers/AzureCommunicationServices/AcsProvider.cs
  HealthChecks/EmailProviderHealthCheck.cs
  EmailSendException.cs
```

- `OutboundEmail.From(EmailMessage, resolvedFrom, expires)` is the single mapping point. `IsBodyHtml` decides `HtmlBody` or `TextBody`. 1.0 doesn't invent a text part.
- Postmark is plain `HttpClient` (named client `community.emailSender.postmark`), with the `X-Postmark-Server-Token` header and no redirects.
- ACS uses the `Azure.Communication.Email` SDK (Q3) [DATED: version picked at scaffold], with its default log redaction kept on.
- The composer uses `builder.Services.AddUnique<...>()` for both services. A container test proves the replacement wins over core's factory registration.

### 4.3 Logging

- At Information, per send: provider, `emailType` when known, recipient **count**, attachment count, provider message id and elapsed ms.
- At Warning, on failure: provider, HTTP status and provider error code.
- Never: addresses, subject, body, token, connection string, request or response bodies.
- `IHttpClientFactory` logs only the request URI at Information [unverified]; neither provider's URI carries a secret.

---

## 5. Security notes

- **API keys live in config or env vars only.** They are never in the database, never in `appsettings.json` committed to a repo, and never in logs, exception text or health-check output. Options `ToString()` is redacted, and a leak test with sentinel values covers logs, exceptions and the health-check description.
- **No recipient addresses in logs** (GDPR: an address is personal data). Counts only.
- **Key scope:** the README recommends a Postmark **server** token (not the account token) and an ACS resource used only for this site, so a leak is contained. It also links each provider's rotation steps.
- **Invite and reset links** are bearer secrets. Turning link tracking off stops the provider rewriting them (Postmark), which also stops click-tracking redirects from logging them.
- **Supply chain:** one external SDK (Azure). Lock files are committed, and osv-scanner and check-vulnerable run on every PR.
- Accepted risk: an exception thrown inside core's invite flow surfaces to the admin as core's generic error. That's the same as SMTP today.

---

## 6. Test strategy

- **Unit (xUnit):**
  - The mapper: From fallback order, html/text, cc/bcc/reply-to, the `Expires` header, the attachment cap.
  - Postmark: request shape through a recording `HttpMessageHandler`, error mapping for 401, 422 and 500 and for a timeout.
  - ACS: request shape through the SDK's `HttpClientTransport` over a recording handler.
- **Sender tests:** notification-first order, `IsHandled` short-circuits, `CanSendRequiredEmail` is true.
- **Container test:** a real `UmbracoBuilder`, with and without `Provider`, resolving both services.
- **Leak test:** sentinel token, address and subject must never appear in captured logs (`FakeLogger`), exception messages or `ToString()`.
- **Both lines:** every suite runs with `-p:UmbracoMajor=17` and `18` (`scripts/test.sh`). `ScaffoldTests` asserts the built major, and a `#if UMBRACO_17` test calls the obsolete overload.
- **Manual (last plan task), on a 17.8 and an 18.3 demo site:**
  - a real send from the demo site through Postmark (a test server, or the `POSTMARK_API_TEST` token, which accepts and discards [DATED]) and through an ACS test resource;
  - an invite, a password reset and a Forms "send email" workflow.

---

## 7. Release

- Pascal reserves the NuGet ID with an empty `0.0.1-alpha`, then unlists it.
- First real release `17.0.0-alpha.1` and `18.0.0-alpha.1` from one tag (`release/0.0-alpha.1`), then `17.0.0` and `18.0.0`.
- NuGet and Marketplace tags: `umbraco`, `umbraco-marketplace`, `email`, `smtp`, `azure-communication-services`, `postmark`, `transactional-email`.
- Repo `PascalA94/Umbraco.Community.EmailSender`, **public at scaffold**:
  - CodeQL, a branch ruleset, gitleaks, and secret scanning with push protection;
  - `docs/` holds `design.md`, `plan.md` and `spikes.md` only.

---

## 8. Open questions for Pascal

| # | Question | Default |
|---|---|---|
| Q1 | Second provider: Postmark or Resend? | **Postmark**: per-message link-tracking off (the invite-link problem) and a free test token for CI-free manual checks |
| Q2 | When a provider and `Smtp:PickupDirectoryLocation` are both set (common in dev), who wins? | **The provider wins**, with one start-up warning. Unset `Provider` in Development to keep the pickup folder |
| Q3 | ACS: use the `Azure.Communication.Email` SDK, or hand-roll REST with HMAC signing? | **SDK**: hand-rolled request signing is a security risk and isn't worth saving one dependency |

## Scope change (Pascal, 2026-11-04)

- **Add GOV.UK Notify as a provider.** See the scope.md section "Added provider". The plan needs a Notify provider task (template-based, `((subject))`/`((body))`, no attachments) before the final "exercise it for real" task. Use a Notify trial-mode key in the test.
