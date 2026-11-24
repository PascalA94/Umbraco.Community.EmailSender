# spikes

Findings that differ from, or add to, the design. Newest last.

## M0 (scaffold, both lines)

- **Layout:** the template's `src/` + `demo/` layout is kept (tests are in `src/<name>.Tests`, not `tests/`), with one demo site that follows `-p:UmbracoMajor`, instead of two demo projects. One project, one lock file per line, same coverage.
- **Lock files:** `packages.17.lock.json` and `packages.18.lock.json` beside every csproj. The floor leg restores them in locked mode.
- **osv-scanner ignores renamed lock files.** It only recognises `packages.lock.json`, so `--recursive` alone scans none of ours. CI passes each one explicitly with `-L packages.lock.json:<path>` (8 files, 62 to 221 packages each; confirmed locally).
- **`dotnet list package --vulnerable` audits the last restore**, not the `-p:UmbracoMajor` argument (it rejects `-p:`). `check-vulnerable.sh` therefore runs after a restore of each line.
- **Latest leg (17.\*):** restores unlocked and the suite passes locally (3 unit and 2 integration tests). The 18.\* latest leg is the same mechanism and is first verified on GitHub.
- **Package reference:** `Umbraco.Cms.Infrastructure` only (plain class library, no Razor, no ASP.NET framework reference).
- **Unit tests follow the package's range, not 17.\*/18.\*:** the unit-test project has no direct Umbraco reference, so the latest leg floats only the integration tests and the demo. Accepted.
- **Check names:** the `osv` job is a reusable workflow, so its check run is named `osv / <job>` and is diff-only on PRs. Read the real names off the first PR before setting the ruleset. `check-vulnerable` is the absolute gate and now runs without waiting for `osv`.
- **Umbraco floor bumps are manual** (Dependabot cannot update the `$(UmbracoRange)` property or the renamed lock files). No `global.json`: CI uses `10.x`.

## M2 (providers: Postmark and Azure Communication Services)

- **Postmark address format** [verified against the Email API reference, 2026-11-05]: `From` is `Full Name <addr>` (max 255 chars); `To`, `Cc`, `Bcc` and `ReplyTo` are comma-separated strings; at most 50 recipients across To, Cc and Bcc together (the provider refuses more before sending); a `Headers` entry is `{Name, Value}`. A 200 carries `ErrorCode` 0 and a `MessageID`. The reference does not document error bodies or the per-email size limit, so the provider parses only a numeric `ErrorCode`.
- **ACS has a per-message tracking switch.** Design 3.2 said it was a domain setting [unverified]. `Azure.Communication.Email` 1.1.0 has `EmailMessage.UserEngagementTrackingDisabled`, and the provider sets it when `DisableLinkTracking` is true. The domain-level setting still decides when the message does not carry it, so the README keeps the advice to turn off user engagement tracking on the domain. The health check no longer needs a tracking warning.
- **ACS SDK version:** `Azure.Communication.Email` 1.1.0 (`Azure.Core` 1.49.0, `System.ClientModel` 1.7.0 in the lock files). Both lines resolve the same versions.
- **ACS sender is a bare address.** A display name in the From setting is dropped.
- **ACS retries are off** (`Retry.MaxRetries = 0`): the SDK's default would resend a POST that may already have been accepted, which sends an invite twice. Timeout is `EmailSender:TimeoutSeconds`, applied by the provider.
- **ACS test double:** the SDK reads its response through a seekable stream and then throws `ObjectDisposedException` on an in-memory body. The tests answer with a non-seekable stream, as a socket would. Not seen in production code paths.
- **Health checks:** Postmark `GET /server` (200 Success, 401 "Postmark rejected the server token.", other statuses Error). ACS fetches the endpoint root with no credentials, so any answer proves DNS and TLS only; the text says sending is not proven.
- **HTTP clients:** both named clients use `SocketsHttpHandler` with `AllowAutoRedirect = false` and an infinite `HttpClient.Timeout`. The ACS client also gets an infinite handler lifetime and a 5-minute pooled-connection lifetime, because the SDK holds one `HttpClient` for the life of its `EmailClient`.
- **ACS connection string:** one strict parser (`endpoint` and a base64 `accesskey`, each once, no other segments). The endpoint must be https on a `*.communication.azure.com`, `.us` or `.cn` host (no IP literal, no user info), and the client is built from the parsed parts, never the raw string, so the SDK's more lenient parser and its value-echoing errors are never reached. There is deliberately no override setting: config keys are permanent once released. A sovereign cloud on another suffix needs a patch release.
- **Timeouts:** `EmailSender:TimeoutSeconds` must be 1 to 300 (a larger value overflows the timer). The ACS SDK's own 100 s network timeout is raised above that so the provider's message wins.
- **Factory logging:** both named clients redact every logged header value, because `IHttpClientFactory` logs headers at Trace and the Postmark token and the ACS signature are header values.
- **Unverified, for Task 11:** whether ACS accepts a custom `Expires` header on invites and resets; whether the SDK's own network timeout really bypasses the provider timeout at 100 s (raised defensively, not reproduced).

## M3 (GOV.UK Notify, health check, leak tests, docs)

- **Notify API** [recalled from the public API documentation and the official clients, not yet exercised against a live service; check in Task 11]: `POST /v2/notifications/email` takes one `email_address`, `template_id`, `personalisation` and an optional `email_reply_to_id`; 201 returns an `id`. Errors are `{"errors":[{"error":"BadRequestError","message":"..."}],"status_code":400}`. Auth is a bearer JWT (HS256, `iss` = service id, `iat` = now, valid for about 30 seconds) signed with the key's secret. The key is `{name}-{service id}-{secret}` with the last two parts 36-character GUIDs. The token is built by hand (about 15 lines) rather than adding a JWT dependency.
- **Notify limits shape the provider:** one recipient per request (so one request per `To`, cap 50), no attachments, Cc, Bcc or free-form reply-to, and the From is fixed in the service. Attachments, Cc and Bcc fail before sending; a message reply-to is ignored with a warning; `ReplyToId` selects a reply-to registered in Notify.
- **HTML is reduced to text.** Notify shows HTML as text, and Umbraco's system emails are HTML. `NotifyBodyFormatter` drops head, style, script and comments, keeps `http(s)` anchors as `[text](url)`, turns br and block ends into newlines and decodes entities. Output is never HTML. Layout is lost by design.
- **Health check:** `GET /v2/template/{id}` proves the key and template id without sending, and the template is checked for `((subject))` and `((body))` (a Warning when missing). No response text is copied into the result.
- **Error text:** only the HTTP status and an error class name matching `^[A-Za-z]{1,64}$` are echoed. The "team-only key / trial mode" refusal gets a fixed hint chosen by substring match, never the response text.
- **No `From` for Notify:** `OutboundEmail.Create` takes `fromRequired`, and `IEmailProvider.UsesFromAddress` is false for Notify, so a site need not set a meaningless `EmailSender:From`.
- **Health check discovery:** Umbraco discovers health checks by type scan whether or not the composer registered a provider, so `EmailProviderHealthCheck` takes `IServiceProvider` and resolves `IEmailProvider` lazily. A constructor dependency would break every health check on a site with no provider. With no provider it reports Info.
- **Leak tests:** the theory crosses every provider with nine failure paths (the fake server echoes the sentinel address, subject, token and body). A mutation check (a deliberate address in a Postmark log line) made six cases fail, so the theory does read the provider's own logger.
- **Unverified, for Task 11:** Notify live behaviour (trial-mode key, `((subject))` in the subject line, the 30-second token window, whether Notify accepts a very large `((body))`).
- **Factory logging removed (found by the real-pipeline leak test):** `IHttpClientFactory` logs request headers at Trace. The rendered text honours `RedactLoggedHeaders`, but the structured log state keeps the raw header value, so a structured sink would receive the credential. All three named clients now use `RemoveAllLoggers()` instead of `RedactLoggedHeaders`, and `LeakTests.RealHttpPipeline_LogsNoCredentialHeaderAtTrace` fails if that is undone. This supersedes the M2 "factory logging redacted" note.
- **Markdown in Notify personalisation** [recalled; verify in Task 11]: Notify renders Markdown links in `((body))`. Square brackets in free text (HTML or plain) are replaced by round ones so editor-controlled text cannot form a link; generated `[text](url)` links are kept through marker substitution. Line-start syntax (`#`, `^`, `-`) is not neutralised; layout spoofing is accepted until the live check shows how Notify renders it.
- **Partial failure:** any failure after the first recipient says "Sent to N of M recipients", whatever the cause. `TimeoutSeconds` is per request, so the worst case is 50 x `TimeoutSeconds` (deferred).

## M6

- **Fixture scrub (Task 12, 2026-11-16):** besides the Mandrill `key` values and the Graph `access_token` named in the plan, gitleaks flagged three more doc placeholders: the Graph `client_secret` in `graph/token-request.txt` (now `TEST-ONLY-NOT-A-SECRET`) and the Mailgun `--user api:...` lines (now `api:$MAILGUN_API_KEY`, which gitleaks reads as an env reference). `gitleaks dir` over `Fixtures/` then prints no leaks.
- **Postmark on the pipeline (Task 15):** the unconfirmed Sender Signature answers HTTP 422, but its `ErrorCode` is undocumented [unknown, carried]. That 422 shows status and code only; the documented pending-approval codes 412 and 413 carry fixed hints. Rate-limit numbers, headers and `Retry-After` are undocumented, so a 429 without `Retry-After` backs off 1 s then 2 s. Postmark has no idempotency key, so a 500 or a connection failure is never retried. The conformance F3 for Postmark uses ErrorCode 412.
- **Read cap:** the shared pipeline reads 64 KB of every response (was 4 KB for Postmark and Notify errors), per design 9.2 step 4. Only allow-listed codes are ever echoed, so the larger read changes no error text.
- **Host-wide HTTP defaults (M6 security review):** a site's `ConfigureHttpClientDefaults` (Aspire ServiceDefaults' resilience handler, any site-wide Polly policy) wraps every named client, so it would resend provider POSTs on 5xx, 429 and its own timeouts, bypassing the design 9.2 table and the ACS no-retry ruling. Every provider client now clears additional handlers (`ConfigureAdditionalHttpMessageHandlers`); a registry-driven test checks every provider, with the defaults registered before and after the package.

## M7 (wave A: SendGrid, Resend, Mailgun, Brevo, Mailjet)

Each provider is built from its research note and checked against recorded examples. None has been run live. Unknowns below are carried with the safe default (design 9.11).

- **Allowed-host check (M6 deferred minor, done first):** `ProviderCapabilities.AllowedHosts` lists every host a mapper may address, all over https. The pipeline refuses any other host before sending ("<Provider> built a request for a host it is not allowed to use. Nothing was sent."), so a mapper that builds a host from options cannot send the credential elsewhere. A registry-driven test requires every pipeline mapper to declare it. Postmark and Notify were updated.
- **SendGrid:**
  - The `X-Message-Id` header is undocumented [unknown]: the id is read from it when present, else "none".
  - No `Retry-After` is documented, so a 429 backs off 1 s then 2 s.
  - The size cap is 20 MB because the docs say both 20 MB and 30 MB.
  - A non-ASCII From display name is dropped (the address stays).
  - Any 2xx is accepted (200 appears only in sandbox mode).
  - Error `errors[].message` is never read.
- **Resend:**
  - The error body shape comes from the official SDK source, not a docs example (fixtures are `.synthesised`); only `name` is echoed, and only when it is a short token.
  - The per-request Cc and Bcc caps are undocumented: the shared 50 applies.
  - The 409 idempotency errors are not special-cased (status only).
  - A quota 429 (`daily_quota_exceeded`, `monthly_quota_exceeded`) is not retried.
- **Mailgun:**
  - Error bodies and the unverified-sender signal are undocumented: status and a fixed hint only, F3 not applicable.
  - `h:Reply-To` is inferred from the `h:` prefix rule, not shown on the docs page.
  - The 25 MB cap is applied to the multipart body (raw bytes, no base64); Mailgun's own count of a MIME-encoded message may be stricter.
  - Whether a Domain Sending Key can be made on the free plan is unknown.
  - 420 is retried like 429.
- **Brevo:**
  - Size caps are undocumented, so only the package cap applies.
  - A `duplicate_parameter` error is assumed to be HTTP 400 (the status is undocumented). It is accepted with id "none" and a Warning only on a retry that carried our own key.
  - Header policy: all dropped, `idempotencyKey` kept in the body's `headers`.
  - Tracking is an account setting.
- **Mailjet:**
  - The HTTP status of a failed message inside a 2xx is undocumented: the error's own `StatusCode` is shown, never its text.
  - The 401 and 429 bodies are undocumented: status only.
  - The per-message recipient cap is unknown (shared 50).
  - Regions are unknown: one host.
  - An unreadable or `Messages`-less 2xx fails closed.
- **Pipeline change:** `IProviderMapper.ReadOutcome` gained an overload that takes the `SendAttempt` (default: ignores it). Only Brevo overrides it.
- **Conformance suite changes:** a multipart provider's shape match compares form part names with the curl fixtures (`RecordedEmail.PartNames`, `FixtureLoader.CurlPartNames`); `ProfileExpectations.BinaryAttachments` sizes F11 without base64 growth; an empty `SuccessBody` means an empty response (SendGrid).
- **Leak sweep found a real gap:** `LeakTests` configured only the three built providers, so a new provider ran with no secret set and proved nothing. Options now come from each provider's profile, every profile secret joins the sentinel list, every options class is printed, and the clock is a `SteppingClock` (a provider with an idempotency key waits before retrying a 500, which hung on a frozen fake clock).
- Two older tests used "Mailgun" as an example of an unknown provider name; they now use "Mailchimp".

## M8 (wave B: Bird, MailerSend, SMTP2GO, Elastic Email, Mandrill, Mailtrap)

Same rules as M7: built from the research notes and recorded examples, none run live, unknowns carried with the safe default (design 9.11).

- **Start-up warnings (Task 22):** `IProviderMapper.StartupWarnings` (default none) and one `ProviderStartupWarnings` handler log each selected mapper's fixed-text warnings once. A key prefix is a hint, never a failure, and the text never carries the key. Bird uses it first; SMTP2GO reuses it.
- **Bird:**
  - The 401 body is a synthesised shape and its `code` is `E0xxxx` [unknown]: the code is echoed only when it is a short token.
  - 422 is both a field error (`validation_error`) and an unverified sender domain (a delivery or business-rule type [docs]); only `error.type == delivery_error` gets the verify-the-domain hint. If Bird names the type differently, F3 shows status and code only.
  - 421 shows the shared error text with a fixed region hint (the plan quoted a bespoke sentence; the shared format keeps one shape for every provider).
  - Free-plan caps are described both as 429 and as "rejected" [unknown]: every 429 is treated as a rate limit, so a daily cap retries up to the budget and then fails. No quota stop.
  - The combined To, Cc and Bcc cap is unknown (shared 50); `Expires` may be a reserved header [unknown, design 9.13]; the optional live check finds out.
  - `from` is sent as the object `{email, name}`, the form in Bird's own basic example (review finding: a string with a display name is unverified).
- **MailerSend:**
  - Success is a 202 with an empty body and the id in `x-message-id` only. A 202 with no id and a readable body (the `ALL_SUPPRESSED` warning, whose shape is not documented) fails with a fixed hint; an empty or unreadable body fails closed.
  - `x-send-paused: true` is accepted with a Warning whether or not `x-message-id` is present: whether a paused 202 carries an id is undocumented [unknown].
  - The 401 body is synthesised [unknown]: status only. The error code is the `#MSnnnnn` suffix of the message or of the first field error; nothing else of the text is read.
  - Several usage limits are 422, not 429 (`MS42204`, `MS42242`, `MS42222`, `MS42225`): they fail at once with status and code. Only `MS42901` is a quota stop; other 429s (`MS42903`) retry.
  - No idempotency key, so a 500 or a connection failure is never retried. No region host is documented, so one host.
  - The trial domain's 10 MB attachment cap is left to the provider's 422 `MS42206` (design 9.13); the package applies 25 MB to the whole request.
- **SMTP2GO:**
  - The API answer for an unverified sender and for a sandboxed key is undocumented [unknown]. Any 2xx with `data.failed` above zero or `data.error_code` present is a failure; a failure text containing "not verified" gets the verify-the-sender hint, searched and never copied. An unreadable 2xx fails closed.
  - Both documented error shapes (nested under `data` and flat) are parsed; only the error code is echoed, and only when it is a short token.
  - No `Retry-After`, rate-limit headers or idempotency key are documented: a 429 backs off 1 s then 2 s, and a 500 or connection failure is never retried. `fastaccept` is never sent.
  - Reply-To goes in `custom_headers` (comma-joined for several addresses); the per-attachment size and count caps are unknown (only the 50 MB request cap applies).
  - Tracking is a per-API-key setting; the `Global` region picks the nearest endpoint by resolver location, so residency needs `US`, `EU` or `AU`.
- **Elastic Email:**
  - Error bodies are undocumented, so a failure is status and a fixed hint only; no part of the body is read. F3 (unverified sender) and F6 (quota) are not applicable: no signal is documented, and a 200 may precede a later suppression.
  - The `Idempotency-Key` header is in the vendor's OpenAPI but not in the older rendered reference (design 9.13 row 17); it is sent on every attempt, so a 500 is retried. If the live API ignores it, a retried 500 could duplicate a send; the optional live check would show it.
  - The 50-recipient and 20 MB caps come from a secondary source (the vendor's skill file); the package caps match them, and attachment count and size are otherwise unchecked.
  - The API is said to return 5xx, not 429, under load [secondary]: those fall under the shared table (500, 502 and 504 retry only because of the key).
  - No EU or UK host is documented, so one host. No inline content-id field exists in the schema.
- **Mandrill:**
  - The key is the body's `key` field. No header carries it; the request records print a fixed name, and a test checks the key is absent from the URL, headers, logs and error text.
  - A 2xx is read per recipient: `sent`, `queued` and `scheduled` are accepted; anything else fails the whole send with a count and the first `reject_reason`, shown only when it is in the documented enum. A non-array, empty or unreadable 2xx fails closed. A "sent" entry that also carries a `reject_reason` (the docs sample does) is still accepted.
  - The error text follows the shared shape ("Mandrill rejected the email: HTTP 200. 1 of 1 recipients rejected (reject_reason: unsigned)."); the plan quoted a bespoke sentence. Errors match on `name` (`Invalid_Key`, `PaymentRequired`), never the numeric `code`.
  - The validation-error shape, any 429, and the unverified-sender behaviour with a test key are undocumented [unknown]: those fall to status and the name when it is a short token.
  - `preserve_recipients` is not sent (default false), so a recipient may not see the To list in a Cc send; whether Cc and Bcc types behave as headers with the default is unverified. `bcc_address` and `images[]` are never used. No idempotency key, so a 500 or connection failure is never retried. Reply-To goes in `headers` (inferred from the field description).
- **Mailtrap:**
  - Only the transactional host is used, never the batch or sandbox hosts. Every attachment is `disposition: attachment` (no content id).
  - A 401 means a bad token or an unverified sending domain [docs], so the hint says both; F3 reuses that signal. Tracking is not in the request schema [unknown]: the README says to check the domain or stream setting.
  - The 429 body and any `Retry-After` are undocumented for the live API (the spec lists 429 for the sandbox only), so a 429 backs off 1 s then 2 s. An over-quota send on the free plan may be refused with another code [unknown]: status and a fixed hint only.
  - `success: false` in a 200 is a failure (defensive). A 2xx with an unreadable body is accepted with id "none" (not fail-closed). One id per recipient: the first is logged.
- **Test rig (found while adding Mandrill):** `FakeProviderServer.InFlight` counted an instant responder for the moment it ran, so the clock driver, sampling mid-answer under load, advanced past the send budget and timed out a healthy send (Mandrill C11, once). It now counts only an answer that is still pending when the responder returns.

## M9 (Microsoft Graph)

Built from the research note and the recorded docs examples, not run live. Unknowns carried with the safe default (design 9.11).

- **Pipeline hooks (Task 28 and 29):** `IProviderMapper` gained `AcquireCredentialAsync` (the token, fetched before each attempt inside the budget and handed to `BuildRequest` as `SendAttempt.Credential`), `RefreshesCredentialOnUnauthorized` and `ForgetCredential` (the pipeline repeats a 401 once with a fresh credential, then fails), and `CheckHealthAsync` (a check that is not one request on the provider's own client). Every other mapper uses the defaults. `SendAttempt.ToString()` hides the credential.
- **Token endpoint:** only the allow-listed `error` code and up to three numeric `error_codes` are echoed; `error_description` never is. A failed token request is not retried (a 429 or 5xx from Entra fails the send); the first 429 from the token endpoint has no documented `Retry-After` handling [unknown].
- **Cache:** one token, replaced once under 5 minutes remain, with a 60 s floor capped at the real lifetime. A settings change drops the token, and a token fetched while the settings changed is used once and not kept. `Invalidate()` drops whatever is cached, so many sends that all get a 401 at once can cause a few extra token requests; they stay serialised and bounded.
- **Status codes:** any 2xx is accepted (docs say 202, OpenAPI says 204; which the live service returns is [unknown]). No message id exists, so the log says "none".
- **Errors:** the code is `error.code` only, when it is a short token. The codes for an unlicensed mailbox, a mailbox outside the RBAC scope and a forbidden sender are undocumented [unknown], so F3 is not applicable and a 403 or 404 gets a fixed hint. A second 401 reads "rejected the email: HTTP 401 ... The token was rejected. Check the app's Mail.Send permission and admin consent." (the shared format with a fixed hint, as in M8, not the design's bespoke sentence).
- **Attachments:** 3 MB each before encoding, 35 MB for the request. Whether `sendMail` takes a larger inline attachment is [unknown].
- **Headers:** `x-` names only, so `Expires` is dropped. The count and length limits of `internetMessageHeaders` are [unknown]. A message From is never sent: one Debug line says the From is not used (this applies to any provider with `UsesFromAddress = false`, GOV.UK Notify included).
- **`saveToSentItems`:** sent as a JSON boolean from the option (default false). The docs example writes the string `"false"`; the API's documented type is Boolean, so the fixture shape check allows the difference.
- **Path segments:** `TenantId` and `SenderUserId` are validated at start-up and again at send time (a reloaded setting is not revalidated, and `..` would change the path), then escaped.
- **Not verified:** the tenant-wide `Mail.Send` risk can only be reduced by the user (RBAC for Applications); the package cannot detect it. Failures after acceptance arrive as NDRs in the mailbox. Residency, `from.emailAddress.name` behaviour and header lookup in Sent Items are unresearched [unknown].
- **503 is not retried (security review, Medium):** design 9.2's table retries a 503 as a not-accepted signal, but design 9.4 says Graph has no idempotency key so a 5xx is never retried. The stricter rule wins: `GraphMapper` marks only a 429 retryable, so a 503 fails at once. Whether a Graph 503 can follow an accepted send is [unknown]; the cost of being wrong is a failed send where a retry would have worked. Conformance C11 is therefore not applicable to Graph.
- **Cache generation and canonical ids:** the cache entry carries the settings generation it was fetched under, so a token stored after a settings change is never served. GUIDs (tenant, client, mailbox) must be in canonical `D` form with no surrounding whitespace, because the raw value becomes a path segment. A token with characters outside RFC 6750 is refused rather than cached. A 401 refresh is never made on the last attempt.
