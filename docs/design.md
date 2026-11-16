# Design: HTTP-API email sender

| | |
|---|---|
| Package | `Umbraco.Community.EmailSender` |
| Owner | Pascal Meyer (`PascalA94`) |
| Size | S, about 5-8 h agent time (4-6 h plus 1-2 h for the second line) |
| Lines | **Both from the start** (Pascal, 2026-11-04: client sites are on v17 LTS): `17.x` on CMS `[17.8.0, 18.0.0)` and `18.x` on `[18.3.0, 19.0.0)`, one codebase, both packed from one commit |
| Status | Draft design, 2026-11-04. Nothing built |
| Inputs | the scope note, the portfolio rules (floors, CI and GitHub rules, Build approach), the Umbraco pitfalls list and an earlier package as house style (all private) |

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

---

## 9. Provider extension (Pascal, 2026-11-16)

Twelve more providers behind the same `IEmailProvider`, on one shared pipeline. Built in M6 to M10 (plan "Provider extension").

**This section supersedes:**
- Section 1 Goals ("two providers in 1.0") and Non-goals ("Resend and SendGrid later", "retries with back-off").
- Section 6 "Manual": the live exercise is Postmark only. Every other live check is optional and Pascal-gated.
- Section 7 tags: the Marketplace tag waits for stable (9.12).

**Sources.** Each provider is built from its research note and recorded examples (fixtures), researched on 2026-11-16 from official docs and OpenAPI files and kept in the private workspace. The research notes are not copied into this repo; the fixtures are (9.5). Tags in this section: **[docs]** read in the provider's docs or OpenAPI on 2026-11-16; **[unknown]** not documented, carried (9.11); **[DATED]** likely to move (prices, free tiers).

**Already decided by Pascal, not re-opened here:** the provider list, raw HTTP (no vendor SDKs), one interface plus a shared pipeline, docs-first verification, the status labels, optional live checks, alpha only. No council was run: the contested choices were Pascal's, and what remains is mechanism.

### 9.1 Provider families

| Family | Providers | Auth carriage | Body |
|---|---|---|---|
| A1: static key in a header, JSON | Postmark (built), Resend, SendGrid, Brevo, MailerSend, SMTP2GO, Elastic Email, Bird, Mailtrap | `X-Postmark-Server-Token`; `Authorization: Bearer` (Resend, SendGrid, MailerSend, Bird, Mailtrap); `api-key` (Brevo); `X-Smtp2go-Api-Key` (SMTP2GO); `X-ElasticEmail-ApiKey` (Elastic Email) [docs] | JSON |
| A2: static key, odd carriage | Mailjet, Mailgun, Mandrill | Basic `public:secret` (Mailjet); Basic `api:KEY` (Mailgun); `key` in the JSON body (Mandrill) [docs] | JSON `Messages[]` envelope (Mailjet); `multipart/form-data` (Mailgun); JSON (Mandrill) |
| Template | GOV.UK Notify (built) | HS256 JWT per request | JSON, one request per recipient |
| SDK | Azure Communication Services (built) | SDK HMAC signing | SDK. Stays outside the shared HTTP retry (M2 ruling: SDK retries off) |
| C: OAuth client credentials | Microsoft Graph | Bearer token from Entra ID, cached (9.4) | JSON `message` object |
| Later | Amazon SES | SigV4 | Not in this plan |

### 9.2 Shared pipeline

**Layout (adds to 4.2):**

```
Providers/Pipeline/IProviderMapper.cs        // per provider: build the request, read the outcome
Providers/Pipeline/ProviderCapabilities.cs   // caps, header policy, reply-to style, tracking, idempotency
Providers/Pipeline/ProviderPipeline.cs       // pre-checks, budget, attempts, retry, logging
Providers/Pipeline/ProviderOutcome.cs        // Accepted(ids) or Rejected(status, code, hint, retryable)
Providers/Pipeline/ResponseReader.cs         // capped read, safe JSON helpers, code allow-list
Providers/Pipeline/ProviderHttpClients.cs    // AddEmailProviderClient(name): the one way to register a client
Providers/Pipeline/PipelineEmailProvider.cs  // IEmailProvider over a mapper
Providers/Registry/ProviderDefinition.cs     // kind, config section, validate, register, capabilities
Providers/<Name>/<Name>Mapper.cs (+ <Name>Request.cs for source-gen JSON types)
```

**Mapper contract (internal):**

```csharp
internal interface IProviderMapper
{
    string Name { get; }
    ProviderCapabilities Capabilities { get; }
    HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt); // idempotency key, attempt number
    ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body);                 // body already capped
    HttpRequestMessage? BuildHealthRequest(EmailSenderOptions options) => null;                          // null = no safe check (9.6)
}
```

`PipelineEmailProvider` implements `IEmailProvider` for every mapper. ACS keeps its own `IEmailProvider` (SDK). Notify moves onto the pipeline with the per-recipient policy.

**One send, in order:**
1. **Pre-checks, before any request** (each fails with `EmailSendException`, nothing sent):
   - Recipients: To + Cc + Bcc at most **50** for every provider (the built Postmark and Notify cap, now package-wide), and at most the provider's own lower caps (MailerSend: Cc 10, Bcc 10 [docs]).
   - Attachments: count at most the provider cap (Bird 20, Mailjet 50 [docs]; Notify none).
   - Size: the request body is serialised to bytes first and refused if over the provider's `MaxRequestBytes` (size table below). Exact, and free: the body is buffered anyway.
2. **Header policy** applied to a copy of `OutboundEmail.Headers` (table below).
3. **Idempotency key**: one GUID per logical send, reused on every attempt, sent only where documented.
4. **Attempt**: a fresh `HttpRequestMessage` per attempt (a request cannot be resent), `ResponseHeadersRead`, body read capped at **64 KB** (Mandrill, Mailjet and Mailtrap return one entry per recipient, which outgrows the old 4 KB cap at 50 recipients).
5. **Outcome**: the mapper decides. A 2xx is not proof of acceptance (body-parse rules below).
6. **Retry decision** (rules below).
7. **Log**: Information on success (provider, recipient count, attachment count, first message id or "none", attempts, elapsed ms). Warning on failure (provider, HTTP status, allow-listed code). Never an address, subject, body, key, token or response text (anti-goals, section 1).

**Budget.** `EmailSender:TimeoutSeconds` (1 to 300, unchanged) is the budget for one provider call **including retries**. For Notify's per-recipient policy it stays per recipient. Single-attempt sends behave exactly as today.

**Retry rules.** Retry only when the provider certainly did not accept the email, or when a documented idempotency key makes a repeat safe. A duplicate invite is worse than a failed one.

| Signal | Retry? |
|---|---|
| 429 | Yes, unless the mapper marks it a quota stop (Resend `daily_quota_exceeded` / `monthly_quota_exceeded`, MailerSend `MS42901`) [docs] |
| 420 (Mailgun recipient rate limit) | Yes [docs] |
| 503 | Yes |
| 500, 502, 504, or a connection failure before any response | Only with an idempotency key (Resend, Bird, Elastic Email by header; Brevo in the body) |
| A timeout (budget spent) | No |
| 401 | No. Graph only: refresh the token once (9.4) |
| Any other 4xx, any 2xx-on-failure | No |
| ACS | Never through the pipeline (SDK path, retries off since M2) |

- **Wait:** `Retry-After` when present (delta seconds or HTTP date). Otherwise 1 s, then 2 s, each with plus or minus 20% jitter. At most 3 attempts.
- **No wait over 10 s, and none past the budget:** fail at once with "<Provider> is rate limiting this site (HTTP 429). Try again later." Umbraco sends invites and resets on the request thread, so a long sleep is a hung backoffice.
- Other rate-limit headers (`X-RateLimit-Reset`, `x-sib-ratelimit-reset`, `RateLimit`) are ignored: formats differ and are partly undocumented (9.11).
- Brevo: a `duplicate_parameter` error on a **retry** that carried our own key means the first attempt was accepted. It maps to accepted, id "none", with a Warning [docs: a duplicate returns an error, not the original response].

**Error text.** One template: `"<Provider> rejected the email: HTTP <status>[, <code>].[ <hint>]"`.
- `<code>` only when it matches `^[A-Za-z0-9_.\-]{1,64}$` (the Notify M3 rule). Never a provider message: Mailjet `ErrorMessage`, Bird `message` and SendGrid `errors[].message` can echo addresses.
- `<hint>` is fixed text per mapper, for example 401 "Check EmailSender:Resend:ApiKey." and unverified sender "Verify the sender or domain in Resend.".
- `EmailSendException` keeps a null `InnerException` (Global Constraints).

**Body-parse rules (2xx is not always success).**

| Provider | Success | Failure hidden in a 2xx | Message id |
|---|---|---|---|
| Postmark | 200 and `ErrorCode` 0 | `ErrorCode` not 0 (defensive) | body `MessageID` |
| Resend | 200 | none [docs] | body `id` |
| SendGrid | 202 | none | header `X-Message-Id` if present [unknown]; else none |
| Mailgun | 200 | none ("Queued" means accepted) | body `id` |
| Brevo | 201 | none | body `messageId` |
| Mailjet | any 2xx | any `Messages[].Status` not `success` (HTTP status of a failed send is [unknown]) | body `Messages[].To[].MessageID` |
| Bird | 202 | none | body `id` |
| MailerSend | 202 with `x-message-id` | 202 with no `x-message-id` (all suppressed, nothing sent). `x-send-paused: true` is accepted with a Warning | header `x-message-id` only |
| SMTP2GO | 200 with `data.failed` 0 | `data.failed` > 0, or `data.error_code` present. `fastaccept` is never sent, so failures are reported | body `data.email_id` |
| Elastic Email | 200 | none documented (later suppression is async) | body `MessageID` |
| Mandrill | 200, every entry `sent`, `queued` or `scheduled` | any entry `rejected` or `invalid`: "Mandrill rejected N of M recipients (reject_reason: <allow-listed enum>)" | body `[]._id` |
| Mailtrap | 200 with `success: true` | `success: false` (defensive; only `/api/batch` is documented 2xx-on-failure, and it is not used) | body `message_ids[]` |
| Graph | any 2xx (docs say 202, OpenAPI says 204) | none | none |
| Notify | 201 | none | body `id` |

For the four documented 2xx-on-failure providers (Mailjet, MailerSend, SMTP2GO, Mandrill), an unparseable 2xx body **fails closed**: "<Provider> answered HTTP 200 but the result could not be read." Elsewhere a 2xx with an unreadable body is accepted with id "none".

**Recipients.**
- Batch policy (every provider but Notify): one request, To, Cc and Bcc as given, at most 50 in total.
- Per-recipient policy (Notify): one request per `To`; a partial failure reports "Sent to N of M recipients" (M3, unchanged).
- MailerSend accounts awaiting approval allow 1 recipient each [docs]. Not pre-checked (the account state is invisible); the provider's 422 `MS42205` gets a fixed hint.

**Reply-To.**

| Style | Providers |
|---|---|
| Array of addresses | Resend `reply_to`, Bird `reply_to[]` (1 to 25), SendGrid `reply_to_list`, Graph `replyTo[]` |
| Single object | Brevo `replyTo`, MailerSend `reply_to`, Mailjet `ReplyTo`, Mailtrap `reply_to` |
| Comma-separated string | Postmark `ReplyTo`, Elastic Email `Content.ReplyTo` |
| Custom header | SMTP2GO `custom_headers` `Reply-To`, Mandrill `headers.Reply-To` |
| `h:` form part | Mailgun `h:Reply-To` (standard header, not shown on the page [docs]) |
| Not possible | Notify (ignored with a Warning, M3) |

Single-object providers send the first Reply-To and log one Warning with the count dropped. An invite never fails over a Reply-To.

**Custom headers (core only ever adds `Expires`).**

| Policy | Providers |
|---|---|
| Send as given | Postmark, Resend, SendGrid, Mailgun (`h:`), Mailjet, Bird, SMTP2GO, Elastic Email, Mandrill, Mailtrap |
| Drop all, one Debug line | Brevo ("standard email headers are not supported" [docs]); MailerSend (custom headers need a Professional plan, else 422 `MS42233` on every send [docs]) |
| `x-` names only | Graph (`internetMessageHeaders` names must start with `x-` [docs]) |

Bird rejects "reserved" headers with 422 but does not list them [unknown]. `Expires` is sent. If it is reserved, every Bird send fails, and the optional Bird live check (plan Task 33) is where that would show.

**Tracking (`DisableLinkTracking`, default true).**

| Per request? | Providers and field |
|---|---|
| Yes | Postmark `TrackLinks`/`TrackOpens`; SendGrid `tracking_settings`; Mailgun `o:tracking*`; Mailjet `TrackOpens`/`TrackClicks` = `disabled`; Bird `track_opens`/`track_clicks` (**default true**, so false is always sent when the switch is on); MailerSend `settings.track_*`; Elastic Email `Options.Track*`; Mandrill `track_opens`/`track_clicks`; ACS `UserEngagementTrackingDisabled` |
| No: account, domain or key setting | Resend (domain, off by default), Brevo (account; pixel removal is Enterprise only), SMTP2GO (per API key), Mailtrap (not in the schema [unknown]). The README says where to switch it off |
| Not applicable | Graph (no tracking), Notify |

**Bird category.** Always `category: "transactional"`. Inline sends default to `marketing`, which blocks on every suppression and opt-out [docs]. A conformance assertion, not an option.

**From.** `OutboundEmail` gains `FromAddress` and `FromName?`, parsed once in `Create` beside the existing `From` string. Object-shaped providers use the parts. SendGrid drops a non-ASCII display name ("Unicode encoding is not supported for the from field" [docs]). Graph and Notify set `UsesFromAddress = false`: the mailbox or the service decides the sender (9.4).

**Inline images.** Umbraco's `EmailMessage` has no content id, so every attachment goes as a normal attachment (disposition `attachment` where the field exists), never inline. Each mapper still records `InlineContentId` in its capabilities (none: Brevo, Elastic Email; file name only: SMTP2GO, Mailgun; content id: the rest [docs]), so a future inline feature can fail clearly where it is missing. The suite asserts both.

**Size caps (`MaxRequestBytes`, after base64).** The package cap `MaxTotalAttachmentBytes` (raw bytes, default 10 MB, at most 50 MB) still applies first.

| Provider | Cap used | Basis |
|---|---|---|
| Postmark | 10 MB request | [docs]. The default 10 MB raw cap already exceeds this after base64 (9.13) |
| Resend | 40 MB | per email after base64 [docs] |
| SendGrid | 20 MB | docs say 20 MB in one place and 30 MB in another; the lower is used [docs] |
| Mailgun | 25 MB | message size [docs] |
| Brevo | package cap | [unknown] |
| Mailjet | 15 MB | whole payload [docs] |
| Bird | 20 MB | after base64 and MIME [docs] |
| MailerSend | 25 MB | attachments; 10 MB on a trial domain is left to the provider's 422 `MS42206` [docs] |
| SMTP2GO | 50 MB | [docs] |
| Elastic Email | 20 MB | secondary source only [unknown] |
| Mandrill | 10 MB request | JSON per call [docs] |
| Mailtrap | 10 MB | per message, all plans by default [docs] |
| Graph | 3 MB per attachment | the add-attachment limit; whether `sendMail` takes more inline is [unknown]; 35 MB per message |
| ACS | package cap | [unknown] |

**HTTP clients.** `AddEmailProviderClient(name)` is the only registration path. It sets `RemoveAllLoggers()` (M3 finding: structured logs keep raw header values), `SocketsHttpHandler { AllowAutoRedirect = false }`, an infinite `HttpClient.Timeout` (the pipeline owns the budget), `Accept: application/json`, and `User-Agent: Umbraco.Community.EmailSender/<version>` (Resend and Mailtrap reject requests without one [docs]). The host is built per request from current options, so a region change is read at send time. Client names: `community.emailSender.<camelName>`.

**Mailgun.** `MultipartFormDataContent`: one part per recipient and per header, attachments as file parts with their content type. `o:`, `h:` and `v:` parts share a 16 KB cap [docs]; `Expires` is far below it.

**Mailjet.** Exactly one message in `Messages[]`; `SandboxMode` never sent.

### 9.3 Options and start-up validation

```json
"EmailSender": {
  "Provider": "Resend",
  "Resend":         { "ApiKey": "<secret>" },
  "SendGrid":       { "ApiKey": "<secret>", "Region": "Global" },
  "Mailgun":        { "ApiKey": "<secret>", "Domain": "mg.example.com", "Region": "US" },
  "Brevo":          { "ApiKey": "<secret>" },
  "Mailjet":        { "ApiKey": "<public key>", "SecretKey": "<secret>" },
  "Bird":           { "ApiKey": "<secret>", "Region": "us1" },
  "MailerSend":     { "ApiKey": "<secret>" },
  "Smtp2Go":        { "ApiKey": "<secret>", "Region": "Global" },
  "ElasticEmail":   { "ApiKey": "<secret>" },
  "Mandrill":       { "ApiKey": "<secret>" },
  "Mailtrap":       { "ApiKey": "<secret>" },
  "MicrosoftGraph": { "TenantId": "<guid or domain>", "ClientId": "<guid>", "ClientSecret": "<secret>", "SenderUserId": "<guid or address>", "SaveToSentItems": false }
}
```

- **Provider values** (new `EmailProviderKind` names, permanent once released, matched case-insensitively): `SendGrid`, `Resend`, `Mailgun`, `MicrosoftGraph`, `Brevo`, `Mailjet`, `Bird`, `MailerSend`, `Smtp2Go`, `ElasticEmail`, `Mandrill`, `Mailtrap`. The section name equals the value.
- **One name for the main secret: `ApiKey`.** The README says what each provider calls it (Mailtrap "API token"; Mailjet's private key goes in `SecretKey`).
- **Secrets come only from configuration**: env vars or a secret store in production, `dotnet user-secrets` in development. Never a committed `appsettings.json`, never the database. Every secret is `[JsonIgnore]` and `***` in `ToString()` (section 5).
- **Validation (`ValidateOnStart`; failures name the key, never the value):**
  - Every secret: required when its provider is selected; no control or non-ASCII characters (a trailing newline from a mounted secret).
  - Documented key prefixes give a start-up **Warning**, never a failure (formats change): Bird `bk_<region>_` or `bm_<region>_`, also warned when the prefix names the other region (Bird answers 421 to a mismatch); SMTP2GO `api-` [docs].
  - Mailgun `Domain`: a bare host name (no scheme, slash or port). It becomes a URL path segment.
  - Graph `TenantId`: a GUID or a DNS name; `ClientId`: a GUID; `SenderUserId`: a GUID or an address. All escaped as path segments.
  - `Region`: an enum; an unknown value fails, naming the allowed list.
- **Regions, only where the provider documents them:**

| Provider | Values | Host | Note |
|---|---|---|---|
| SendGrid | `Global` (default), `EU` | `api.sendgrid.com`, `api.eu.sendgrid.com` | EU needs Pro or Premier, an EU subuser, an EU IP and a key made on that subuser [docs] |
| Mailgun | `US` (default), `EU` | `api.mailgun.net`, `api.eu.mailgun.net` | Must match the domain's region. AU is contract-only, so not offered [docs] |
| Bird | `us1` (default), `eu1` | `{region}.platform.bird.com` | Fixed per organisation at sign-up; the key must match [docs] |
| SMTP2GO | `Global` (default), `US`, `EU`, `AU` | `api`, `us-api`, `eu-api`, `au-api` on `smtp2go.com` | `Global` picks the nearest by resolver location [docs] |

No region option elsewhere. Resend picks the sending region per domain on one host, and its docs say all account data is stored in the US whatever the region [docs]. The README makes no residency claim where a provider's docs say nothing.

### 9.4 Microsoft Graph

**Flow.** Client credentials: `POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token` (form: `client_id`, `client_secret`, `scope=https://graph.microsoft.com/.default`, `grant_type=client_credentials`), then `POST https://graph.microsoft.com/v1.0/users/{SenderUserId}/sendMail` with `{"message":{...},"saveToSentItems":false}` [docs].

**Token cache (`GraphTokenSource`, singleton):**
- One cached token, refreshed when less than **5 minutes** of `expires_in` remain (60 s minimum lifetime used). There is no refresh token in this flow; just ask again [docs].
- A `SemaphoreSlim` so concurrent sends make one token request and share it. The wait counts against the send budget.
- An options change (`IOptionsMonitor.OnChange`) drops the cached token.
- A 401 from `sendMail` drops the token and retries **once** with a fresh one (a 401 is not an accepted send). A second 401 fails: "Microsoft Graph rejected the token. Check the app's Mail.Send permission and admin consent."
- The token is opaque: never parsed as a JWT, never logged, never in exception or health-check text [docs].
- Token endpoint failures echo only the allow-listed `error` code (for example `invalid_client`) and the numeric `error_codes`, never `error_description` (it can carry tenant and app ids).
- Two named clients, `community.emailSender.graphToken` and `community.emailSender.graph`, both through `AddEmailProviderClient`. Public cloud hosts only; sovereign clouds are Later (a new config key).
- Client secret only. Certificate credentials are Later (better for production; the README says so).

**Mapping.**
- The sender is the mailbox in `SenderUserId`; `UsesFromAddress = false`. A message From that differs is ignored with one Debug line (Forms workflows set their own From). The mailbox's display name is used.
- One body: `HTML` or `Text` (Graph takes one, as `OutboundEmail` holds one).
- Headers: `x-` names only, so `Expires` is dropped.
- Attachments: `#microsoft.graph.fileAttachment`, at most 3 MB each before encoding.
- Any 2xx is accepted. No message id. Failures after acceptance arrive as NDRs in the mailbox, which the README says.
- 429 honours `Retry-After` [docs]. No idempotency, so 5xx is never retried.

**Health check.** Acquire a token. Success text: "Microsoft Graph issued a token. Mail.Send and the mailbox scope are not proven without sending."

**Security note for the README (required, before the config sample).** Application `Mail.Send` lets the app send as **any** mailbox in the tenant [docs].
- Restrict it with **RBAC for Applications** in Exchange Online, scoped to a management scope holding only the sending mailbox. Then remove the tenant-wide Entra `Mail.Send` grant, because the two are additive [docs].
- Application Access Policies are the legacy route, replaced by RBAC for Applications [docs].
- Keep the client secret in a secret store with a short expiry; prefer a certificate when that ships.
- A trial tenant on `onmicrosoft.com` reaches at most 100 external recipients a day [docs, DATED].

### 9.5 Conformance suite

One suite for every provider. A provider is done when its profile passes every scenario.

**Layout (tests live under `src/`, per the M0 spike note):**

```
src/Umbraco.Community.EmailSender.Tests/Conformance/
  IProviderProfile.cs        // kind, options with sentinel secrets, fixture folder, expectations, Normalise()
  RecordedEmail.cs           // provider-neutral view of one recorded request
  ConformanceTests.cs        // [Theory] profiles x scenarios
  FixtureLoader.cs
  Profiles/<Name>Profile.cs  // one per provider, ACS and Notify included
src/Umbraco.Community.EmailSender.Tests/Fixtures/<name>/   // copied from the workspace, scrubbed, with SOURCE.md
```

- **Profiles.** Each turns a recorded request (JSON, multipart, or the ACS SDK's recorded JSON) into a `RecordedEmail`: from, to, cc, bcc, reply-to, subject, body, headers, attachments, tracking flags, idempotency key, and where the credential sat. It declares its expectations: caps, header policy, reply-to style, tracking style, idempotency, id location, body-parse rule.
- **Fixtures.** Copied once from the workspace `providers/fixtures/<name>/` into `Fixtures/<name>/` with each `SOURCE.md`, and copied to the test output. SES is not copied (Later).
  - **Scrub on copy:** Mandrill `key` values and the Graph `access_token` become `TEST-ONLY-NOT-A-SECRET`. Then `gitleaks dir` over the folder must show no leaks (portfolio CI rule 7; JSON cannot carry `// gitleaks:allow`).
  - **Request fixtures** are compared by **shape**, not bytes: for the matching scenario, every field we send exists in the fixture at the same path with the same JSON type, and every field the fixture shows as required is present. Fields we add on purpose (Bird `category`, tracking flags) are listed in the profile.
  - **Response fixtures** are what the fake server returns in the success and error scenarios.
  - `*.synthesised.*` fixtures (built from docs prose, not a published example) carry `[Trait("Fixture", "synthesised")]` and are named in the README caveats.
  - **No fixture for a case** (Mailgun errors, Elastic Email errors): the scenario serves an empty body and asserts status-only mapping. Carried, not invented.

**Scenarios (every provider):**

| # | Scenario | Asserts |
|---|---|---|
| C1 | Minimal: from, one to, subject, text | shape match with `request-basic`; credential only in the documented place |
| C2 | HTML body | HTML field used; no empty text part (SendGrid refuses an empty `value`) |
| C3 | Display name on From | kept, or dropped where documented (SendGrid non-ASCII, ACS, Graph, Notify) |
| C4 | Cc, Bcc, two Reply-Tos | reply-to style per 9.2; single-object providers send the first and warn |
| C5 | `Expires` header | header policy per 9.2 |
| C6 | One attachment | base64 round trip, file name, content type; shape match with `request-attachment` |
| C7 | Image attachment | a normal attachment, never inline; capability flag as in 9.2 |
| C8 | Tracking off | field per 9.2, or nothing for account-level providers |
| C9 | Unicode subject and file name | intact (Review Focus 5) |
| C10 | Success | id read from the documented place and logged; "none" where there is none |
| C11 | Idempotency | same key on every attempt where supported; no key elsewhere |
| C12 | User-Agent and Accept | on every request |

**Failure scenarios (every provider):**

| # | Scenario | Asserts |
|---|---|---|
| F1 | Bad key (`response-error-auth`) | auth text with the key-name hint; no secret anywhere |
| F2 | Validation error (`response-error-validation`) | status and allow-listed code only |
| F3 | Unverified sender | clear text with the verify hint, from the provider's own signal (Resend 403, SendGrid 403, Mailjet 403 `send-0008`, Bird 422, MailerSend 422 `MS42207`, Postmark 422, Mailtrap 401, Mandrill 200 `rejected`/`unsigned`) [docs] |
| F4 | 429 with `Retry-After: 1`, then success | one retry after the wait; success logged with 2 attempts |
| F5 | 429 with `Retry-After: 60` | no wait; immediate "rate limiting" failure |
| F6 | Quota 429 (Resend, MailerSend) | no retry |
| F7 | 500 | retried only where an idempotency key exists, with the same key |
| F8 | 2xx-on-failure body (Mailjet, MailerSend with no id, SMTP2GO, Mandrill; Mailtrap and Postmark defensive) | `EmailSendException`, not success |
| F9 | Unparseable 2xx body | fails closed for the four 2xx-on-failure providers; accepted elsewhere |
| F10 | 51 recipients; the provider's lower cap | refused before any request |
| F11 | Request over `MaxRequestBytes` | refused before any request; text names the provider limit |
| F12 | Hang | timeout text after the budget; no retry |
| F13 | 302 redirect | not followed; credential not resent (the M2 test, now shared) |
| F14 | 1 MB response | read stops at 64 KB; no crash |
| F15 | Leak sweep | across every scenario: sentinel key, address, subject and body absent from `FakeLogger` records (structured state included), exception text and health-check text |

**Provider-specific extras** stay in `<Name>MapperTests.cs`: Mailgun 420 and multipart parts, Mandrill per-recipient counting, Brevo duplicate-on-retry, Bird region and `category`, Graph token cache (9.4), the Notify formatter (built).

**Registry guard.** A test fails if any `EmailProviderKind` value lacks a `ProviderDefinition` or a conformance profile, so no provider skips the suite or the leak sweep.

### 9.6 Health check per provider

| Provider | Check | Result |
|---|---|---|
| Postmark | `GET /server` (built) | Success or Error |
| Notify | `GET /v2/template/{id}` (built) | Success, Warning or Error |
| ACS | DNS and TLS only (built) | Success, "sending not proven" |
| Graph | token request | Success, "Mail.Send not proven" |
| Every other provider | none: no documented read-only call proves a sending-only key [unknown] | **Info**: "<Provider> is configured. Credentials are not checked without sending." |

The Info result is deliberate: a check that needs a full-access key would push users to widen their key.

### 9.7 Status labels

| Label | Rule |
|---|---|
| **Verified live** | Pascal ran the plan's live check for that provider on both lines and it is recorded in `docs/spikes.md` (date, versions, what was sent, what arrived) |
| **Built from the provider's docs and checked against recorded examples** | The conformance profile passes every scenario, and the provider has a non-synthesised `request-basic` fixture and a documented success signal |
| **Unverified** | Built and unit tested, with no recorded examples (ACS: SDK path) and no live check |
| **Experimental** (on top of a label) | Not proven where it matters most: GOV.UK Notify until someone with public-sector access tests it |

- Labels at the first alpha: Postmark "Verified live" (or "Built from the provider's docs..." if Task 11 cannot run, said plainly); ACS "Unverified" plus the retirement line; Notify "Unverified, Experimental"; every new provider, Graph included, "Built from the provider's docs and checked against recorded examples".
- A label moves up only with evidence in `docs/spikes.md`. The README never calls a provider "supported" beyond its label.

### 9.8 README provider guides

A status table first (provider, `Provider` value, label, link to its caveats), then one short section per provider with exactly:
1. **Where to get the key**, and the narrowest key type that can send.
2. **Config sample**: env-var form first (`EmailSender__Resend__ApiKey`), then JSON with `<secret>` placeholders. Never a real-looking value.
3. **Sender or domain verification** the provider needs before it sends.
4. **Free tier**, dated "as of November 2026".
5. **Caveats**: where tracking is switched off, Reply-To or header limits, region, retirement.

Facts per provider, from the research notes [docs; DATED for prices and tiers]:

| Provider | Key | Verification | Free tier (Nov 2026) | Caveats |
|---|---|---|---|---|
| Postmark | Server token (not the account token) | Confirmed Sender Signature; while the account is pending approval, recipients must share the From domain | Developer plan, 100 a month, no card | Activity keeps invite and reset links (M4 deferred minor) |
| ACS | Connection string of a resource used only by this site | Verified domain on the resource | Azure pricing | **Microsoft retires ACS Email on 30 Sep 2028**; also turn off user engagement tracking on the domain |
| GOV.UK Notify | Live or team API key | Service in Notify; one template with `((subject))` and `((body))` | Public sector only | Experimental; no attachments, Cc or Bcc; HTML becomes text |
| SendGrid | API key with Mail Send scope | Single Sender Verification or Domain Authentication | No free plan; 60-day trial at 100 a day | EU needs Pro, an EU subuser key and `Region: EU` |
| Resend | API key with sending access | `onboarding@resend.dev` sends to your own address only; anything else needs a verified domain | 3,000 a month, 100 a day | Tracking is a domain setting (off by default); docs say account data is stored in the US whatever the sending region |
| Mailgun | Domain Sending Key | Sandbox domain with up to 5 authorised recipients, or a verified domain | 100 a day | `Domain` required; `Region` must match the domain |
| Brevo | API key (v3) | Validated sender or authenticated domain; account approval before sending | 300 a day after approval, no card | Tracking is account-level; custom headers (`Expires`) not sent; "Block unknown IP addresses" can refuse the site |
| Mailjet | API key (public) and Secret Key | Validated sender or domain | 6,000 a month, 200 a day, no card | One Reply-To |
| Bird | Workspace key (`bk_...`) | `onboarding@messagebird.dev` to workspace members only, or a verified domain | 1,000 a month, 50 a day, no card | `Region` fixed at sign-up; system emails go as `transactional` |
| MailerSend | API token for the domain | Verified domain or the trial domain | 500 a month; card and account approval | 1 recipient each until approved; custom headers dropped (Professional plan); one Reply-To |
| SMTP2GO | API key (`api-...`) | Verified domain or single sender (not Gmail or Yahoo) | 1,000 a month, 200 a day; work email and SMS code at sign-up | Tracking is set per API key; regional hosts |
| Elastic Email | API key with SendHttp access | Verified domain or sender; the sign-up address is pre-verified | 100 a day in the free phase | Error bodies undocumented, so errors show status only |
| Mandrill | Mandrill API key | Verified domain with SPF and DKIM (not Gmail or Yahoo) | No free plan; Mailchimp Standard or a Transactional block | Per-recipient results; one rejected recipient fails the send |
| Mailtrap | API token (Email Sending, not Sandbox) | Verified domain, or the demo domain to your own address | 4,000 a month, 150 a day | Tracking not set per request; one Reply-To |
| Microsoft Graph | Entra app: tenant id, client id, client secret | Licensed Exchange Online mailbox; `Mail.Send` application permission with admin consent | Needs a Microsoft 365 tenant | The RBAC-for-Applications note (9.4) comes first; failures after acceptance arrive as NDRs |

### 9.9 Security review points

For the M6 and M9 milestone reviews and the final review (security-analyst):
1. **Retry safety**: no retry after a possibly accepted send without a documented idempotency key; keys reused across attempts and unique per send.
2. **Credential carriage**: each secret only where its provider documents it (header, Basic, Mandrill body). Mandrill's body-carried key never reaches a log, exception or `ToString()`. Basic credentials built from parts, never logged.
3. **Redirects**: none followed on any client, so no credential reaches another host.
4. **Responses**: capped reads; only allow-listed codes echoed; provider message text never surfaces.
5. **Graph**: cache concurrency, expiry skew, the single 401 refresh, no token or `error_description` in output, path escaping of tenant and mailbox, the README warning about tenant-wide `Mail.Send`.
6. **URL building**: Mailgun `Domain`, Graph `TenantId` and `SenderUserId` validated and escaped; region hosts from a fixed enum, never free text.
7. **Fixtures**: scrubbed before commit; no real addresses or keys.
8. **Header injection**: CR/LF already stripped by `OutboundEmail`; the header policy runs after it.
9. **Leak sweep** covers every registered provider (registry guard).
10. **Supply chain**: still one SDK (ACS); no new package for the 12 providers.

### 9.10 Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Maintaining 15 providers alone | High | Small mappers on one pipeline; honest labels; a provider change shows up as one failing profile. A provider that rots can be dropped in a major |
| Doc drift (fields, limits, errors change) | Medium | Fixtures keep `SOURCE.md` with dates; mapping leans on status first; re-read a provider's note before touching its mapper |
| 2xx-on-failure bugs (a "sent" that never was) | Medium | Body-parse rules per provider; fail closed where 2xx-on-failure is documented; F8 and F9 for every provider |
| Duplicate sends from retries | Low | Retry rules in 9.2; ACS stays on no-retry |
| Graph scope too wide in a client tenant | Medium | README security note first; the package cannot enforce RBAC |
| Docs-only providers used in production | Medium | Labels in the status table and every guide; alpha only |
| Bird treats `Expires` as reserved | Low | Optional Bird live check; a one-line header-policy change if so |

### 9.11 Unknowns carried, not chased

An unknown in a provider note is **not** researched further or guessed during the build. It gets the safe default, and the README caveats name it where a user would notice.
- **Non-2xx is a failure**: status first, allow-listed code second, nothing else.
- **Retry-After only**: other rate-limit headers are ignored.
- **Unknown size caps**: the package cap.
- **Unknown error bodies**: status only.
- **Unknown residency**: no claim.
- **Unknown health-check endpoints**: Info (9.6).
- **Unknown reserved headers** (Bird): send; the optional live check finds out.
- A build session that meets an unknown records it in `docs/spikes.md` and carries on with the default. It never signs up for anything or asks for credentials outside the plan's Pascal-gated steps.

### 9.12 Release (adds to section 7)

- Alpha tags only until Pascal says otherwise: `17.0.0-alpha.1` and `18.0.0-alpha.1` from one `release/0.0-alpha.1` tag.
- **No Marketplace listing before stable.** The Marketplace finds packages by the `umbraco-marketplace` NuGet tag [unverified whether it lists prerelease-only packages], so the alpha ships without that tag; it comes back at stable. `umbraco-marketplace.json` stays in the repo.
- NuGet tags gain the provider names that fit: `sendgrid`, `resend`, `mailgun`, `brevo`, `mailjet`, `microsoft-graph`.

### 9.13 Conflicts with the built code, and the smallest fix

| # | Conflict | Smallest fix | Task |
|---|---|---|---|
| 1 | The repo `docs/` copy was path-scrubbed and had Task 6b; the workspace copy had local paths and no Task 6b. A plain overwrite would leak paths into a public repo and drop 6b | Done 2026-11-16: the workspace copy was brought level with the repo copy, and new text carries no local paths. The refresh is a verbatim copy plus a grep | 12 |
| 2 | Retries vs the M2 ruling "ACS retries off: a resent POST can send an invite twice" | Retry only on not-accepted signals or with an idempotency key (9.2); ACS stays off the pipeline retry | 14 |
| 3 | `TimeoutSeconds` is per request today | Budget per provider call including retries; same as now for single attempts; Notify stays per recipient | 14 |
| 4 | `OutboundEmail` always adds `Expires`; MailerSend (free plan), Brevo and Graph refuse it | Per-provider header policy (9.2) | 14, 20, 23, 29 |
| 5 | The 50 cap lives inside Postmark and Notify; ACS has none (M3 deferred minor) | One pipeline cap of 50 (To + Cc + Bcc) for every provider, ACS included, plus provider lower caps | 14, 16 |
| 6 | "Per-recipient send up to 50" is read as the shared cap of 50 plus Notify's per-recipient policy, not a fan-out for every provider (a fan-out would break Cc and Bcc) | Assumption stated; Pascal to confirm (plan open question) | 14 |
| 7 | The default `MaxTotalAttachmentBytes` of 10 MB raw is about 13.3 MB after base64, over Postmark's, Mandrill's and Mailtrap's 10 MB caps | Keep the default; the per-provider `MaxRequestBytes` pre-check gives a clear message instead of a 413 | 14 |
| 8 | `OutboundEmail.From` is one string; object-shaped providers need address and name | Add `FromAddress` and `FromName` beside `From` (internal type) | 14 |
| 9 | `EmailSenderOptions` and the validator `switch` would grow by 12 cases | `ProviderDefinition` registry: each provider brings its section, validation and registration; the composer `switch` becomes a lookup. Existing keys unchanged | 13 |
| 10 | `EmailProviderKind` is public and its names are config values | Append 12 names (9.3). The parser matches by name, so order does not matter. The 3 built names stay | 13, 17 to 29 |
| 11 | The 4 KB response cap is too small for per-recipient result arrays | 64 KB on success bodies; errors still read only allow-listed fields | 14 |
| 12 | `IEmailProvider.CheckAsync` assumes a meaningful check exists | Info result where no safe check exists (9.6) | 14 |
| 13 | Task 11 needs ACS and Notify credentials | Narrowed to Postmark; ACS and the free providers move to optional Task 33; Notify waits for someone with public-sector access | 11, 33 |
| 14 | Section 7 tags include `umbraco-marketplace` | Left off the alpha (9.12) | 30 |
| 15 | Fixtures hold key-shaped strings (Mandrill `key`, Graph `access_token`) | Scrub on copy (9.5) | 12 |
| 16 | The repo's `docs/` may hold only design, plan and spikes | Fixtures go under the test project as test data; the research notes stay in the private workspace | 12 |
| 17 | The matrix summary lists idempotency for Resend, Bird and Brevo only; Elastic Email's note documents an `Idempotency-Key` header in its OpenAPI | Use it for Elastic Email too: the provider note is the more specific source | 25 |
