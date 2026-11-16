# HTTP-API Email Sender Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Execution is **native, plus one final whole-branch `qa-reviewer` review** (Task 10). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Umbraco.Community.EmailSender` `17.0.0-alpha.1` and `18.0.0-alpha.1` from one commit. Both send every core Umbraco email through Azure Communication Services or Postmark, chosen in appsettings.

**Architecture:** A plain class library. When `EmailSender:Provider` is set, a composer replaces both `IEmailSender` (core's version only calls the client when SMTP is configured) and `IEmailSenderClient`. The client maps `EmailMessage` to one `OutboundEmail` and hands it to a singleton `IEmailProvider`.

**Tech Stack:** .NET 10, `Umbraco.Cms.Infrastructure` `[17.8.0, 18.0.0)` and `[18.3.0, 19.0.0)`, `Azure.Communication.Email`, xUnit, NSubstitute, `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`), GitHub Actions, gitleaks, osv-scanner, CodeQL.

**Spec:** `docs/design.md`. Portfolio CI and GitHub rules (private) win over this plan.

**Repo:** `PascalA94/Umbraco.Community.EmailSender`. `SRC` = `src/Umbraco.Community.EmailSender`, `TESTS` = `tests/Umbraco.Community.EmailSender.Tests`.

## Global Constraints

- **Both lines from the start** (Pascal, 2026-11-04). `net10.0`, `eng/lines.txt` = `17` and `18`.
  - `UmbracoMajor` `17` gives `Umbraco.Cms.Infrastructure [17.8.0, 18.0.0)`; `18` (the default) gives `[18.3.0, 19.0.0)`.
  - One lock file per line: `NuGetLockFilePath` = `packages.$(UmbracoMajor).lock.json`.
  - Version is `$(UmbracoMajor).$(PackageMinor).$(PackagePatch)`. A `release/<minor>.<patch>[-suffix]` tag packs both lines from one commit.
  - `scripts/test.sh` runs `dotnet test -p:UmbracoMajor=<n>` for each line. Expected, once per line: `Passed!  - Failed:     0`.
  - `UMBRACO_17` code is only the obsolete `SendAsync(EmailMessage)` overload.
- **Config section `EmailSender`**, with keys and defaults from spec 3.1, verbatim. `Provider` values are `AzureCommunicationServices` and `Postmark`, case-insensitive.
- **Provider unset:** the composer registers nothing.
- **Never log or put in exception text, health-check text or `ToString()`:** API keys, connection strings, recipient addresses, subjects, bodies.
- `EmailSendException` has a null `InnerException` when the inner one could carry the request.
- **Pitfalls:**
  - P1 and P2 [Critical]: no statics. Providers are singletons over `IHttpClientFactory` and `IOptionsMonitor`.
  - P7: no network calls at start-up.
- **CI** (README rules):
  - PRs run build and tests on a four-leg matrix (lines 17 and 18 × floor and latest), gitleaks over every PR commit, osv-scanner with `--config=./osv-scanner.toml`, and check-vulnerable.
  - CodeQL runs through default setup.
  - `release-checks.yml` is `workflow_dispatch` only.
  - No NuGet cache in `publish.yml`. Dependabot `open-pull-requests-limit: 0`. Every action is SHA-pinned.
  - Fake secrets carry `// gitleaks:allow`.
- **CI and GitHub rules (Pascal, 2026-11-04):** the portfolio README.md section "Build approach → CI and GitHub rules" overrides this plan's CI and GitHub tasks: public repo before the first CI push, fast PR checks only, e2e and SQL Server in a manual release-checks.yml, no credentials in workflows (random per-run passwords), e2e locators scoped to visible elements, and the first GitHub CI run planned as its own step before any release tag.
- **Claude never commits or pushes.** Every commit is a "Checkpoint (Pascal commits)" with a suggested message and no AI attribution.

## Review Focus

1. **A site with no `Smtp:Host` at all** still offers and sends invites. Test: Task 4 `CanSendRequiredEmail_TrueWithoutSmtp`.
2. **An email with an empty `From`** and only `Smtp:From` set uses `Smtp:From`; with neither set, the error names both keys. Test: Task 3 `From_FallbackOrder` theory.
3. **A recipient list with blanks or nulls** (`To` is `string?[]`): blanks are dropped, and an email with no valid `To` fails before sending. Test: Task 3 `Recipients_NullsAndBlanksDropped`.
4. **A provider that hangs:** the send fails after `TimeoutSeconds` with a clear message, and no thread is held forever. Test: Task 5 `Timeout_ThrowsEmailSendException`.
5. **A non-ASCII subject or attachment file name** (`Café menu.pdf`) arrives intact. Test: Task 5 `Unicode_SubjectAndFileName_RoundTrip`.

---

### Task 1: Clone and scaffold, with CI and gitleaks from day one

**Files:** `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.gitattributes`, `LICENSE` (MIT, Pascal Meyer), `SECURITY.md`, `src/Umbraco.Community.EmailSender.slnx`, `SRC/*.csproj`, `TESTS/*.csproj`, `TESTS/ScaffoldTests.cs`, `eng/lines.txt`, `osv-scanner.toml`, `.github/workflows/{build,release-checks}.yml`, `.github/dependabot.yml`, `docs/{design,plan,spikes}.md`, `demo/Umbraco.Community.EmailSender.DemoSite/`

- [ ] **Step 1 (Pascal):** create the empty **public** repo `PascalA94/Umbraco.Community.EmailSender` (no README, no licence).
  - Set About: description, NuGet link, topics `umbraco`, `umbracopackage`, `dotnet`, `email`, `postmark`, `azure-communication-services`.
  - Turn on secret scanning with push protection, CodeQL default setup, and squash-merge only.
- [ ] **Step 2:** `gh repo clone PascalA94/Umbraco.Community.EmailSender`. Confirm `git config user.email` is the GitHub noreply address, and don't change it. Copy only `design.md` and `plan.md` into `docs/`, and start `docs/spikes.md`. Expected: `ls docs` shows `design.md plan.md spikes.md`.
- [ ] **Step 3:** write `Directory.Build.props`, modelled on Jira's `AutomateMajor` switch:
  - `RestorePackagesWithLockFile=true`;
  - `UmbracoMajor` (`17` or `18`, default `18`), setting `DefineConstants` `UMBRACO_$(UmbracoMajor)`, `UmbracoRange`, `NuGetLockFilePath` and the version prefix;
  - `UmbracoFloat` for the latest legs.
  - `eng/lines.txt` holds `17` and `18`. Add `scripts/test.sh` and `scripts/pack.sh`, which loop over `eng/lines.txt`. The csproj references `Umbraco.Cms.Infrastructure` at `$(UmbracoRange)`, with tags from spec 7.
- [ ] **Step 4:** write `ScaffoldTests.Assembly_TargetsSelectedMajor` (asserts the `AssemblyMetadata("UmbracoMajor")` value equals the test project's own `UmbracoMajor`, and the referenced `Umbraco.Cms.Core` assembly major matches). Run `./scripts/test.sh`. Expected: FAIL. Add the metadata item, then re-run. Expected: `Passed!` twice, and `packages.17.lock.json` and `packages.18.lock.json` beside each csproj.
- [ ] **Step 5:** two demo sites, `demo/DemoSite.V17` (`Umbraco.Templates::17.8.0`) and `demo/DemoSite.V18` (`18.3.0`), each referencing `SRC` with `UmbracoMajor` set to match. Set `Umbraco:CMS:WebRouting:UmbracoApplicationUrl` to the HTTPS demo URL (README rule 4: invites on 18.3+ need it). Unlike rule 4's default, there is **no** Smtp pickup folder, because this package replaces SMTP (spec Q2).
  - `dotnet new umbraco`, SQLite. Set `UmbracoUseAppLocalIcu=false` before the template conditions (README rule 5).
  - Unattended admin `admin@example.test`, with the password in `dotnet user-secrets` only.
  - Add a `ProjectReference` to `SRC`.
- [ ] **Step 6:** `build.yml`:
  - `permissions: {}` at the top;
  - jobs `build` (matrix `line: [17, 18]` × `leg: [floor, latest]`; floor restores locked, latest uses `UmbracoFloat=true` and no lock file), `secrets` (gitleaks CLI over the PR range, pinned tarball SHA-256) and `deps` (osv-scanner, `build/check-vulnerable.sh`, which parses JSON and never trusts the exit code);
  - NuGet cache on.
  - `release-checks.yml`: `workflow_dispatch` only (no `pull_request`, no schedule; README rules 3, 12, 13).
    - Each line's demo site boots on HTTPS (`dotnet dev-certs https`, `--urls https://localhost:<port>`, readiness through `curl -sk`) against SQL Server started with `docker run` in a step, not a `services:` block.
    - The SQL Server and demo-login passwords come from `openssl rand -base64 24` per run, are masked with `::add-mask::` and passed through `$GITHUB_ENV`. No literal credential is in any workflow.
    - It runs the integration tests against SQL Server, then a smoke check: the site boots, the package's health check runs, and no Error appears in the log. No real provider calls are made in CI.
    - NuGet cache on.
    - Run `actionlint`. Expected: no output.
- [ ] **Step 7:** pre-public check: `gitleaks dir . --redact` shows no leaks; `git grep -nE "ServerToken|ConnectionString" -- ':!*.md'` shows key names only.
- [ ] **Checkpoint (Pascal commits):** `chore: scaffold v18 line, demo site and CI`. Push a branch, open a PR, confirm the checks are green in about 5 minutes, then add the ruleset requiring `build`, `secrets` and `deps`.

### Task 2: Options, validation and the composer

**Files:** `SRC/Configuration/{EmailSenderOptions,EmailSenderOptionsValidator,EmailProviderKind}.cs`, `SRC/EmailSenderComposer.cs`, `TESTS/ComposerTests.cs`, `TESTS/OptionsTests.cs`

**Interfaces:**
- Produces `EmailSenderOptions { EmailProviderKind? Provider; string? From; bool DisableLinkTracking = true; int TimeoutSeconds = 30; long MaxTotalAttachmentBytes = 10485760; AcsOptions AzureCommunicationServices; PostmarkOptions Postmark }`.
- `PostmarkOptions { string? ServerToken; string MessageStream = "outbound" }`, `AcsOptions { string? ConnectionString }`.
- Secrets are `[JsonIgnore]`, and `ToString()` prints `***`.

- [ ] **Step 1:** write the failing tests:
  - `ComposerTests.NoProvider_CoreServicesRemain` (a real `UmbracoBuilder` plus `AddCoreServices`; `IEmailSender` is `EmailSender`, `IEmailSenderClient` is `BasicSmtpEmailSenderClient`);
  - `ComposerTests.Postmark_ReplacesBoth`;
  - `OptionsTests.MissingSecret_FailsNamingKey` (message contains `EmailSender:Postmark:ServerToken` and not the sentinel `pm-SENTINEL` value);
  - `OptionsTests.UnknownProvider_ListsAllowedValues`;
  - `OptionsTests.ToString_Redacts`.
- [ ] **Step 2:** run them. Expected: FAIL (types missing).
- [ ] **Step 3:** implement. The composer reads `EmailSender:Provider` from `builder.Config`. If it is set, it calls `AddOptions<EmailSenderOptions>().BindConfiguration("EmailSender").ValidateOnStart()`, the validator, and `AddUnique` for `IEmailSender` and `IEmailSenderClient` (Task 4 types; stub them now).
- [ ] **Step 4:** run them. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: options and provider-gated composer`

### Task 3: `OutboundEmail` mapping

**Files:** `SRC/Providers/{OutboundEmail,OutboundAttachment}.cs`, `TESTS/OutboundEmailTests.cs`

**Interfaces:** Produces `static OutboundEmail OutboundEmail.Create(EmailMessage message, string? optionsFrom, string? smtpFrom, TimeSpan? expires, long maxAttachmentBytes, TimeProvider clock)`. It returns a record with `From`, `To`, `Cc`, `Bcc`, `ReplyTo` (each `IReadOnlyList<string>`), `Subject`, `HtmlBody?`, `TextBody?`, `Attachments` (`Name`, `ContentType`, `Base64`), and `Headers`. It throws `EmailSendException` on a bad From, no recipients or the size cap.

- [ ] **Step 1:** write the failing tests:
  - `From_FallbackOrder` theory: message, then options, then smtp, then none, with the exact spec 3.2 text;
  - `Recipients_NullsAndBlanksDropped`;
  - `NoRecipients_Throws`;
  - `IsBodyHtml_PicksHtmlOrText`;
  - `Expires_AddsRfc1123Header` (fake clock 2026-11-04T10:00Z plus 1 h gives `Wed, 04 Nov 2026 11:00:00 GMT`);
  - `Attachments_OverCap_ThrowsBeforeReadingAll`;
  - `ContentType_FromFileExtension` (`FileExtensionContentTypeProvider`, default `application/octet-stream`).
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement. Addresses are validated with `MailboxAddress.TryParse` (MimeKit is already transitive through core).
- [ ] **Step 4:** run them. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: normalise EmailMessage to OutboundEmail`

### Task 4: `HttpApiEmailSender` and `HttpApiEmailSenderClient`

**Files:** `SRC/{HttpApiEmailSender,HttpApiEmailSenderClient,EmailSendException}.cs`, `SRC/Providers/IEmailProvider.cs`, `TESTS/HttpApiEmailSenderTests.cs`

**Interfaces:**
- Consumes `OutboundEmail.Create` (Task 3).
- Produces `interface IEmailProvider { string Name { get; } Task SendAsync(OutboundEmail email, CancellationToken ct); Task<HealthCheckStatus> CheckAsync(CancellationToken ct); }`.
- `HttpApiEmailSenderClient : IEmailSenderClient` implements `SendAsync(EmailMessage, TimeSpan?)`, plus `#if UMBRACO_17` `SendAsync(EmailMessage)`.
- `HttpApiEmailSender : IEmailSender` takes `IEventAggregator`, `IEmailSenderClient`, `IOptionsMonitor<GlobalSettings>` and `ILogger`.

- [ ] **Step 1:** write the failing tests:
  - `CanSendRequiredEmail_TrueWithoutSmtp`;
  - `EnableNotification_PublishesFirst`;
  - `HandledNotification_SkipsClient`;
  - `NotificationDisabled_StillSends` (the content-notification path);
  - `SmtpAlsoConfigured_LogsOneWarningAtStartup`;
  - `Client_PassesExpiresThrough`;
  - `#if UMBRACO_17` `ObsoleteOverload_DelegatesWithNullExpiry` (runs only on the 17 line).
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement. Mirror core's `SendAsyncInternal` order (spec 4.1) without the SMTP or pickup branches. Add a why-comment linking spec section 0.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines, including Task 2's `Postmark_ReplacesBoth` with the real types.
- [ ] **Checkpoint (Pascal commits):** `feat: IEmailSender and client that bypass the SMTP gate`

### Task 5: Postmark provider

**Files:** `SRC/Providers/Postmark/{PostmarkProvider,PostmarkRequest}.cs`, `TESTS/PostmarkProviderTests.cs`

**Interfaces:** Produces `PostmarkProvider : IEmailProvider`, named client `community.emailSender.postmark`, base `https://api.postmarkapp.com/`, `AllowAutoRedirect = false`.

- [ ] **Step 1:** write the failing tests, using a recording handler:
  - `Send_PostsExpectedJson`: `POST /email`, header `X-Postmark-Server-Token`, fields `From`, `To` (comma-joined), `Cc`, `Bcc`, `ReplyTo`, `Subject`, `HtmlBody`/`TextBody`, `MessageStream`, `Headers`, `Attachments[{Name,Content,ContentType}]`;
  - `DisableLinkTracking_SetsTrackLinksNone`;
  - `Errors_MapToMessage` theory: 401, 422 with `ErrorCode` 300, 500, giving the spec 3.2 text;
  - `Timeout_ThrowsEmailSendException`;
  - `Unicode_SubjectAndFileName_RoundTrip`;
  - `Redirect_NotFollowed`.
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement with `System.Text.Json` source-gen and `ResponseHeadersRead`. Read at most 4 KB of an error body, and parse only `ErrorCode`.
- [ ] **Step 4:** run them. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: Postmark provider`

### Task 6: Azure Communication Services provider

**Files:** `SRC/Providers/AzureCommunicationServices/AcsProvider.cs`, `TESTS/AcsProviderTests.cs`

**Interfaces:** Produces `AcsProvider : IEmailProvider`. One `EmailClient` is built lazily from the connection string, with `EmailClientOptions.Transport = new HttpClientTransport(factory.CreateClient("community.emailSender.acs"))`.

- [ ] **Step 1:** write the failing tests:
  - `Send_BuildsEmailMessage` (recipients, reply-to, html/plain content, attachments, the `Expires` header; asserted on the recorded request JSON);
  - `Send_UsesWaitUntilStarted` (no long-running poll on the request thread);
  - `RequestFailed_MapsToEmailSendException` (403, 429), with no inner exception.
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement with `SendAsync(WaitUntil.Started, ...)`, and log the operation id at Information. Record the SDK version in `docs/spikes.md`.
- [ ] **Step 4:** run them. Expected: PASS.
- [ ] **Checkpoint (Pascal commits):** `feat: Azure Communication Services provider`

### Task 6b: GOV.UK Notify provider

**Files:** `SRC/Providers/GovUkNotify/{NotifyProvider,NotifyRequest,NotifyApiKey,NotifyBodyFormatter,NotifyServiceCollectionExtensions}.cs`, `SRC/Configuration/*` (kind, options, validator), `TESTS/NotifyProviderTests.cs`

**Interfaces:**
- Produces `NotifyProvider : IEmailProvider`, named client `community.emailSender.govUkNotify`, base `https://api.notifications.service.gov.uk/`, `AllowAutoRedirect = false`.
- Adds `IEmailProvider.UsesFromAddress` (default true; false for Notify) and `OutboundEmail.Create(..., fromRequired)`: Notify needs no From.
- Config: `EmailSender:GovUkNotify:{ApiKey, EmailTemplateId, ReplyToId}`; `Provider` value `GovUkNotify`.

- [ ] **Step 1:** write the failing tests: signed HS256 token (`iss` service id, `iat`); `POST /v2/notifications/email` with `personalisation.subject` and `.body`; one request per recipient; HTML reduced to text keeping http(s) links; attachments, Cc, Bcc and more than 50 recipients fail before sending; message reply-to ignored with a warning; error mapping with no response text; partial-failure count; timeout; health check via `GET /v2/template/{id}`; key and options validation.
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement; hand-rolled HS256 (no JWT dependency); read at most 4 KB of an error body and 64 KB of a template.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `feat: GOV.UK Notify provider`

### Task 7: Health check and leak tests

**Files:** `SRC/HealthChecks/EmailProviderHealthCheck.cs`, `TESTS/{HealthCheckTests,LeakTests}.cs`

- [ ] **Step 1:** write the failing tests:
  - `Postmark_200_Success`, `Postmark_401_ErrorText`, `Acs_BadConnectionString_Error`;
  - `LeakTests.NoSecretsOrRecipients` theory over every failure path in Tasks 2-6. Sentinels `pm-SENTINEL-TOKEN` (`// gitleaks:allow`), `leak@example.test` and subject `SENTINEL-SUBJECT` never appear in `FakeLogger` records, exception messages, health-check text or options `ToString()`.
- [ ] **Step 2:** run them. Expected: FAIL.
- [ ] **Step 3:** implement `[HealthCheck("<new guid>", "Email provider", Group = "Services")]`, delegating to `IEmailProvider.CheckAsync`.
- [ ] **Step 4:** run `dotnet test`. Expected: `Failed: 0`.
- [ ] **Checkpoint (Pascal commits):** `feat: provider health check and leak tests`

### Task 8: Docs, Marketplace and release workflow

**Files:** `README.md`, `CLAUDE.md`, `umbraco-marketplace.json`, `.github/workflows/publish.yml`

- [ ] **Step 1:** README:
  - install, a supported-versions table (`17.x` on CMS 17.8+, `18.x` on CMS 18.3+), the config table (spec 3.1), the env-var form for secrets, per-provider setup;
  - the ACS domain tracking switch, and "core's SMTP check will fail; ignore it";
  - security notes (spec 5) and uninstall.
- [ ] **Step 2:** `CLAUDE.md`, covering behaviours to preserve: provider unset = core; both services are replaced and why; the never-log list; floors.
- [ ] **Step 3:** `umbraco-marketplace.json`, with tags from spec 7.
- [ ] **Step 4:** `publish.yml`: on a `release/*` tag, runs `scripts/pack.sh` (one nupkg per line), trusted publishing, `id-token: write` only there, no NuGet cache, and check-vulnerable before pack. Run `actionlint`. Expected: no output.
- [ ] **Step 5 (Pascal):** reserve the NuGet ID: a scratch `classlib`, `0.0.1-alpha`, push, unlist, 2FA on. Add a Trusted Publishing policy for this repo.
- [ ] **Checkpoint (Pascal commits):** `docs: README, Marketplace and release workflow`

### Task 9: First full GitHub CI run (Pascal, with the agent fixing)

README rule 14: done means green on GitHub, not just locally. Plan time for several fix rounds (01 needed five).

- [ ] **Step 1 (agent):** pre-push checks. `./scripts/test.sh` passes on both lines. `gitleaks dir . --redact` shows no leaks. `ls docs` shows only `design.md plan.md spikes.md`. `git log --all --name-only --format= | grep -iE "scope\.md|review|council"` prints nothing. `grep -rn "uses: " .github/workflows | grep -vE "@[0-9a-f]{40}"` prints nothing. `grep -rnE "password|Password" .github/workflows` shows only `openssl rand` and env-var names. The agent then states plainly in the handover: **CI has not run yet**.
- [ ] **Step 2 (Pascal):** push the feature branch and open a PR. Expected: `build` (four legs), `secrets` and `deps` are green in about 5 minutes.
- [ ] **Step 3 (Pascal):** run **Release checks** by hand on the branch (`gh workflow run release-checks.yml --ref <branch>`). Expected: both lines green (boot, SQL Server and health-check smoke).
- [ ] **Step 4 (agent):** fix each failure under the owning task's tests, one round at a time, until Steps 2 and 3 are green. Record anything that differed between local and GitHub in `docs/spikes.md`.
- [ ] **Checkpoint (Pascal commits):** `ci: fixes from the first GitHub run` (only if anything changed).

### Task 10: Final whole-branch review

- [ ] **Step 1:** dispatch `qa-reviewer` (fresh context, model `opus`) with the branch diff, `docs/*` and the Umbraco pitfalls list. Ask for findings by severity against Global Constraints, Review Focus and spec 3.4.
- [ ] **Step 2:** keep the verdict outside the repo. Fix blocking findings under the owning task's tests, then run `dotnet test`. Expected: `Failed: 0`, CI green.
- [ ] **Checkpoint (Pascal commits):** `fix: final review findings`

### Task 11: Exercise it for real, on 17 and 18, then release

> **Narrowed 2026-11-16 (provider extension):** Postmark only, and it runs in **M10**, after Task 32. ACS moves to optional Task 33 (Step 3 below). The release moves to Task 34 (Step 6 below). Struck text is kept as a record.

- [ ] **Step 1 (Pascal):** a Postmark test server (or the `POSTMARK_API_TEST` token) ~~and an ACS test resource with a verified sender domain~~. Secrets go in `dotnet user-secrets` on the demo site, never in chat or the repo.
  - Options, cheapest first: a Postmark **Sandbox server** (accepts and black-holes mail, shows it as Delivered in Activity); the free Developer plan with a confirmed Sender Signature on a work domain (while pending approval, recipients must share the From domain); or `POSTMARK_API_TEST` (validates only, proves the least).
  - **Pascal-gated: the session asks once which option Pascal chose, then waits until the secret is in user-secrets. It never asks for the value.**
- [ ] **Step 2:** on **both** demo sites (17.8 and 18.3), with no `Smtp:Host` and `Provider: Postmark`:
  - invite a user, reset a password, send a content notification, and run the health-check email;
  - add a Forms "send email" workflow if Forms is installed (records the [unverified] Forms path in `docs/spikes.md`).
  - Expected: each arrives (or shows in the Postmark activity log). Invite links aren't rewritten.
  - **Record in `docs/spikes.md`** (this is what turns the label into "Verified live"): date; Umbraco versions of both demo sites; which Postmark option (Sandbox server, live, or `POSTMARK_API_TEST`); for each email type, sent yes or no and the Postmark MessageID prefix only; whether `Expires` was accepted; whether link tracking stayed off (link unchanged); the health-check result text. No addresses, subjects or tokens.
  - With `POSTMARK_API_TEST` only, the label stays "Built from the provider's docs and checked against recorded examples", and the README says the token run proved validation only.
- [ ] ~~**Step 3:** repeat Step 2 on both sites with `Provider: AzureCommunicationServices`.~~ Moved to Task 33 (optional, Pascal-gated).
- [ ] **Step 4:** negatives:
  - a wrong token gives the 401 text and a health check Error;
  - a 12 MB attachment through a test controller in the demo site only gives the size error;
  - remove `Provider`, and core SMTP behaviour is back.
- [ ] **Step 5:** `grep -rciE "pm-|endpoint=|@example\.test|<subject text>" demo/**/umbraco/Logs` prints only zeros.
- [ ] ~~**Step 6 (Pascal):** run `release-checks.yml`, then push the tag `release/0.0-alpha.1`. Expected: `17.0.0-alpha.1` and `18.0.0-alpha.1` on NuGet from one commit. Run `gh release create --prerelease --generate-notes`. The Marketplace listing goes on at `17.0.0` and `18.0.0`.~~ Moved to Task 34, after the optional checks.
- [ ] **Checkpoint (Pascal commits):** `docs: acceptance results`

## Scope change (Pascal, 2026-11-04)

- **Add GOV.UK Notify as a provider.** See the scope.md section "Added provider". The plan needs a Notify provider task (template-based, `((subject))`/`((body))`, no attachments) before the final "exercise it for real" task. Use a Notify trial-mode key in the test.

---

## Provider extension (Pascal, 2026-11-16): M6 to M10

**Goal:** 12 more providers behind the same `IEmailProvider` (SendGrid, Resend, Mailgun, Brevo, Mailjet, Bird, MailerSend, SMTP2GO, Elastic Email, Mandrill, Mailtrap, Microsoft Graph), on one shared pipeline, checked by one conformance suite, then the first alpha.

**Spec:** `docs/design.md` section 9 (it supersedes sections 1, 6 and 7 where they differ). Each provider's research note and fixtures sit in the private workspace; the session prompt gives the path. Only fixtures come into the repo (Task 12).

**Not re-opened:** the provider list, raw HTTP, the labels, optional live checks, alpha only. These are Pascal's decisions (2026-11-16).

### Milestone map

| Milestone | Tasks | State |
|---|---|---|
| M0 | 1 | Done |
| M1 | 2, 3, 4 | Done |
| M2 | 5, 6 | Done |
| M3 | 6b, 7, 8 | Done |
| M4 | 9 (Pascal), 10 | Done |
| M5 | Presentation and repo settings | Done 2026-11-11 |
| **M6** | 12, 13, 14, 15, 16: docs refresh, registry, shared pipeline, conformance suite, the 3 built providers moved in | Next |
| **M7** | 17 to 21: wave A (SendGrid, Resend, Mailgun, Brevo, Mailjet) | |
| **M8** | 22 to 27: wave B (Bird, MailerSend, SMTP2GO, Elastic Email, Mandrill, Mailtrap) | |
| **M9** | 28, 29: Microsoft Graph | |
| **M10** | 30, 31, 32, 11 (narrowed), 33 (optional), 34: README guides, final review, CI, Postmark live, optional checks, alpha | |

One milestone per session (Token rule 1). Task 11 keeps its number but runs in M10.

### Model and AI hours per milestone

Rates are the portfolio's measured ones: a normal task 0.3 to 0.4 h, a security-heavy or design-heavy task 0.7 h, a first CI run 1 to 2 h (lower here: CI is already green and only new code runs).

| Milestone | Session model | Why | AI hours |
|---|---|---|---|
| M6 | **Opus** | Designs the pipeline and the suite every later task leans on; retry safety and leak rules are security-relevant | 3 to 3.5 h (2 normal tasks, 3 heavy, plus reviews) |
| M7 | **Sonnet** | Five mappers to a fixed recipe against fixtures | 2.5 to 3 h (5 tasks at 0.4 to 0.5 h, plus review) |
| M8 | **Sonnet** | Six mappers to the same recipe | 2.5 to 3 h (6 tasks at 0.35 to 0.45 h, plus review) |
| M9 | **Opus** | OAuth token client, cache concurrency and the tenant-wide `Mail.Send` risk | 1.5 to 2 h (1 heavy task, 1 normal, plus security review) |
| M10 | **Sonnet**, reviewers on **Opus** | README and release mechanics; the whole-branch reviews are dispatched at Opus | 3.5 to 4.5 h, plus 0 to 1 h if Pascal picks optional live checks |
| **Total** | | | **About 13 to 16 h**, plus up to 1 h optional |

Pascal's time on top: about 0.5 h for the Postmark check, about 0.3 h per optional live check he chooses, about 0.5 h for the CI run and release.

### Global Constraints (added for M6 to M10)

- `TESTS` is `src/Umbraco.Community.EmailSender.Tests` (M0 spike: the template layout was kept).
- **One provider task reads only** its research note, its `Fixtures/<name>/SOURCE.md`, design 9.2 and this task (Token rule 4).
- **Raw HTTP only.** No new package reference in `SRC`. The test project needs none either (`FakeTimeProvider` and `FakeLogger` are already referenced).
- **Every client goes through `AddEmailProviderClient`** (design 9.2): no redirects, no factory logging, User-Agent and Accept set.
- **Every `EmailProviderKind` value has a `ProviderDefinition` and a conformance profile.** The registry guard test fails otherwise.
- **Retry rules are design 9.2, verbatim.** ACS is never retried.
- **Unknowns are carried, not chased** (design 9.11): safe default, a line in `docs/spikes.md`, move on.
- **Never sign up for anything, never ask for credentials** outside a step marked **Pascal-gated**. Every live check except Postmark's is optional.
- Fake secrets in C# carry `// gitleaks:allow`. Fixture JSON is scrubbed instead (Task 12).
- Per task: `./scripts/test.sh --filter "FullyQualifiedName~<Name>"`. At milestone end: `./scripts/test.sh` with no filter. Expected, once per line: `Passed!` and `Failed: 0`.
- No local paths in anything committed. `docs/` stays design, plan and spikes only.

### Review Focus (added)

6. **A 429 with a long Retry-After** never sleeps the request thread. Test: conformance F5.
7. **A 2xx that hides a failure** is never logged as sent. Tests: F8, F9.
8. **No retry after a possibly accepted send** without an idempotency key. Test: F7.
9. **Graph token**: concurrent sends make one token request; a 401 refreshes once. Tests: Task 28 `ConcurrentSends_OneTokenRequest`, `Unauthorized_RefreshesOnceThenFails`.
10. **Every registered provider** is in the suite and the leak sweep. Test: `RegistryTests.EveryKindHasDefinitionAndProfile`.

### Open question for Pascal (not blocking)

| # | Question | Default if no answer |
|---|---|---|
| Q4 | "Per-recipient send up to 50": a shared cap of 50 recipients per email (To + Cc + Bcc) plus Notify's existing one-request-per-recipient, or one request per recipient for every provider? | **Shared cap of 50, no general fan-out.** A fan-out would break Cc and Bcc, and core's emails are single-recipient anyway (design 9.13 row 6) |

---

## M6: shared pipeline and conformance suite (Opus)

### Task 12: Refresh the repo docs and import the fixtures

**Files:** `docs/design.md`, `docs/plan.md` (overwritten), `docs/spikes.md` (append only), `TESTS/Fixtures/<name>/*` for 13 providers (not SES), `TESTS/Umbraco.Community.EmailSender.Tests.csproj`, `TESTS/FixtureLoaderTests.cs`

- [ ] **Step 1:** copy `design.md` and `plan.md` from the workspace folder (path in the session prompt) over `docs/`, verbatim. Never touch `docs/spikes.md` beyond appending an `## M6` heading. Expected: `ls docs` shows `design.md plan.md spikes.md`.
- [ ] **Step 2:** `grep -rnE "/Us[e]rs/|~/Do[c]uments|~/Pr[o]jects|~/\.cl[a]ude" docs/`. Expected: prints nothing. If anything prints, stop and tell Pascal: the workspace copy must be fixed there, not in the repo.
- [ ] **Step 3:** copy the workspace `providers/fixtures/<name>/` folders into `TESTS/Fixtures/<name>/` for postmark, resend, sendgrid, mailgun, brevo, mailjet, bird, mailersend, smtp2go, elasticemail, mandrill, mailtrap and graph (with each `SOURCE.md`). Then scrub: every Mandrill `"key"` value and the Graph `access_token` value become `TEST-ONLY-NOT-A-SECRET`.
- [ ] **Step 4:** `gitleaks dir src/Umbraco.Community.EmailSender.Tests/Fixtures --redact`. Expected: `no leaks found`. `grep -rnE "/Us[e]rs/" src/Umbraco.Community.EmailSender.Tests/Fixtures`. Expected: nothing.
- [ ] **Step 5:** write the failing test `FixtureLoaderTests.EveryProviderFolderLoads` (13 folders, each with `SOURCE.md` and a `request-basic.*` file, every `.json` parses). Run `./scripts/test.sh --filter "FullyQualifiedName~FixtureLoader"`. Expected: FAIL (files not in the output).
- [ ] **Step 6:** add `<None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />` to the test csproj. Re-run. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `chore: refresh docs and import provider fixtures`

### Task 13: Provider registry (no behaviour change)

**Files:** `SRC/Providers/Registry/{ProviderDefinition,ProviderRegistry}.cs`, `SRC/Configuration/{EmailSenderOptions,EmailSenderOptionsValidator}.cs`, `SRC/Web/EmailSenderComposer.cs`, `TESTS/RegistryTests.cs`

**Interfaces:**
- Produces `internal sealed record ProviderDefinition(EmailProviderKind Kind, string Section, Action<EmailSenderOptions, List<string>> Validate, Action<IServiceCollection> Register)`.
- Produces `ProviderRegistry.All` and `ProviderRegistry.Get(EmailProviderKind)`.
- Every existing config key, failure message and `Provider` value stays byte for byte the same.

- [ ] **Step 1:** write the failing tests: `RegistryTests.EveryKindHasDefinition`, `RegistryTests.SectionEqualsKindName`, `ComposerTests.UsesRegistryForEveryKind`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Registry|FullyQualifiedName~Composer"`. Expected: FAIL (types missing).
- [ ] **Step 3:** implement. Move the Postmark, ACS and Notify validation cases out of the validator's `switch` into their definitions, unchanged. The composer's `switch` becomes `ProviderRegistry.Get(kind).Register(builder.Services)`.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines, with every existing `OptionsTests` and `ComposerTests` case unchanged. A changed message means the refactor changed behaviour: fix the code, not the test.
- [ ] **Checkpoint (Pascal commits):** `refactor: provider registry`

### Task 14: Shared pipeline

**Files:** `SRC/Providers/Pipeline/{IProviderMapper,ProviderCapabilities,ProviderPipeline,ProviderOutcome,SendAttempt,ResponseReader,ProviderHttpClients,PipelineEmailProvider,ProviderPreChecks}.cs`, `SRC/Providers/OutboundEmail.cs`, `SRC/Providers/IEmailProvider.cs`, `TESTS/Pipeline/{ProviderPipelineTests,RetryTests,ResponseReaderTests,PreCheckTests}.cs`

**Interfaces:**
- Produces `IProviderMapper` (design 9.2, verbatim) and `ProviderCapabilities { int MaxRecipients = 50; int? MaxCc; int? MaxBcc; int? MaxAttachments; long? MaxRequestBytes; long? MaxAttachmentBytesEach; HeaderPolicy Headers; ReplyToStyle ReplyTo; TrackingStyle Tracking; IdempotencyCarriage Idempotency; RecipientPolicy Recipients; InlineContentId Inline; bool FailClosedOn2xx }`.
- Produces `ProviderOutcome.Accepted(IReadOnlyList<string> ids, string? warning)` and `ProviderOutcome.Rejected(int status, string? code, string? hint, bool retryable, bool quotaStop)`.
- Produces `ProviderPreChecks.Run(OutboundEmail, ProviderCapabilities)` (ACS calls it too, Task 16).
- Produces `services.AddEmailProviderClient(string name)`.
- `OutboundEmail` gains `FromAddress` and `FromName`. Nothing else in it changes.
- Fixes the M3 deferred minor: the `<exception>` doc moves back onto `IEmailProvider.SendAsync`.

- [ ] **Step 1:** write the failing tests against a fake mapper and `Recorder`, with `FakeTimeProvider`:
  - pre-checks: `RecipientCap_51_RefusedBeforeSend`, `ProviderLowerCap_RefusedBeforeSend`, `AttachmentCount_RefusedBeforeSend`, `MaxRequestBytes_RefusedBeforeSend_NamesLimit`, `AttachmentEachCap_RefusedBeforeSend`;
  - `HeaderPolicy_SendDropOrXOnly` theory;
  - `IdempotencyKey_SameOnEveryAttempt_UniquePerSend`;
  - retries: `Retry429_HonoursRetryAfterSeconds`, `Retry429_HonoursRetryAfterHttpDate`, `Retry429_NoHeader_BacksOff1Then2`, `Wait_Over10s_FailsAtOnce`, `Wait_PastBudget_FailsAtOnce`, `QuotaStop_NotRetried`, `Status500_RetriedOnlyWithIdempotency`, `ConnectionFailure_RetriedOnlyWithIdempotency`, `MaxThreeAttempts`, `Timeout_IsBudgetAcrossAttempts`;
  - responses: `Read_CappedAt64Kb`, `ErrorCode_AllowList` theory (`MS42207` kept; `a@b.com`, a 65-character code and a newline dropped), `TwoXxFailClosed_UnreadableBody_Throws`, `TwoXx_UnreadableBody_AcceptedWhenNotFailClosed`;
  - clients: `Client_NoRedirect_NoLogging_UserAgentAndAccept`;
  - `From_AddressAndNameParsed`;
  - logging: `Success_LogsCountsIdAttempts_NoPersonalData`, `Failure_LogsStatusAndCode_NoPersonalData`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Pipeline"`. Expected: FAIL (types missing).
- [ ] **Step 3:** implement design 9.2. A fresh `HttpRequestMessage` per attempt. The body is serialised to bytes before the size check. The wait uses the injected `TimeProvider`. Add a why-comment on the retry table linking design 9.2 and the M2 ACS ruling.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines. Postmark and Notify are not on the pipeline yet, so their tests are untouched.
- [ ] **Checkpoint (Pascal commits):** `feat: shared provider pipeline`

### Task 15: Conformance suite, with Postmark as the first profile

**Files:** `TESTS/Conformance/{IProviderProfile,RecordedEmail,ConformanceTests,FixtureLoader,FakeProviderServer,Scenarios}.cs`, `TESTS/Conformance/Profiles/PostmarkProfile.cs`, `SRC/Providers/Postmark/{PostmarkMapper,PostmarkRequest,PostmarkServiceCollectionExtensions}.cs` (`PostmarkProvider.cs` replaced by the mapper)

**Interfaces:**
- Produces `IProviderProfile { EmailProviderKind Kind; string FixtureFolder; EmailSenderOptions Options(); RecordedEmail Normalise(HttpRequestMessage request, byte[] body); ProfileExpectations Expect; IReadOnlyDictionary<string, string> NotApplicable; }`. Every N/A scenario needs a reason string.
- Produces `ConformanceTests` as one `[Theory]` over `ProviderRegistry.All` x the C1 to C12 and F1 to F15 scenarios of design 9.5.
- Postmark keeps its client name, health check, every error text and the `MessageStream` option.

- [ ] **Step 1:** write the harness, the scenarios and `PostmarkProfile`. Shape match: every field we send exists in `Fixtures/postmark/request-basic.json` (or `request-attachment.json`) at the same path with the same JSON type. Response fixtures come from `Fixtures/postmark/`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Conformance"`. Expected: FAIL on the pipeline-only scenarios (F4, F5, F11, F14) because Postmark still has its own send loop.
- [ ] **Step 3:** move Postmark onto `PipelineEmailProvider` with a `PostmarkMapper` (`ErrorCode` not 0 on a 200 is a failure; id from `MessageID`; `MaxRequestBytes` 10 MB). Keep `PostmarkProviderTests`. Delete a case only when a conformance scenario asserts exactly the same thing, and say which in the checkpoint note.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `feat: conformance suite; Postmark on the shared pipeline`

### Task 16: Notify and ACS in the suite; leak sweep and registry guard

**Files:** `SRC/Providers/GovUkNotify/{NotifyMapper,NotifyServiceCollectionExtensions}.cs` (`NotifyProvider.cs` replaced), `SRC/Providers/AzureCommunicationServices/AcsProvider.cs`, `TESTS/Conformance/Profiles/{NotifyProfile,AcsProfile}.cs`, `TESTS/LeakTests.cs`, `TESTS/RegistryTests.cs`

- [ ] **Step 1:** write the failing tests:
  - `NotifyProfile` (per-recipient policy; N/A with reasons: C3 no From, C4 and C6 and C7 expect the existing pre-send failure, C11 no idempotency);
  - `AcsProfile` over the SDK's recorded request JSON (N/A with the reason "SDK path, retries off (M2 ruling)": F4, F7, C11);
  - `AcsProvider_RecipientCap_51_RefusedBeforeSend` (closes the M3 deferred minor);
  - `RegistryTests.EveryKindHasDefinitionAndProfile`;
  - `LeakTests` theory data now comes from `ProviderRegistry.All`, so every provider is swept, ACS in the real-pipeline test included (closes the M4 deferred minor).
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Conformance|FullyQualifiedName~Leak|FullyQualifiedName~Registry|FullyQualifiedName~Acs"`. Expected: FAIL.
- [ ] **Step 3:** implement. Notify moves onto the pipeline with the per-recipient policy; every existing `NotifyProviderTests` and formatter test passes unchanged. ACS calls `ProviderPreChecks.Run` before the SDK and nothing else from the pipeline.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `feat: Notify and ACS in the conformance suite`

**M6 stop.**
- [ ] Full suite: `./scripts/test.sh`. Expected: `Passed!` twice, `Failed: 0`.
- [ ] Dispatch `qa-reviewer` (model `opus`) and `security-analyst` (model `opus`), fresh context, each with `git diff <M5 milestone commit>..HEAD`, the full files it touches, design 9.2, 9.5 and 9.9, and Review Focus 6 to 10. Fix every Critical, High and Important finding under the owning task's tests. A Critical security finding: stop and ask Pascal.
- [ ] Write "Paused here" in `COMMITS-TODO.md` naming Task 17, and the "Milestone commit" block. Stop.

---

## M7: wave A (Sonnet)

**The provider task recipe** (Tasks 17 to 27). Each provider task has these steps; the task lists only what differs.
- **Files:** `SRC/Providers/<Name>/{<Name>Mapper,<Name>Request}.cs`, `SRC/Configuration/<Name>Options.cs`, the definition in `SRC/Providers/Registry/ProviderRegistry.cs`, one name appended to `EmailProviderKind`, `TESTS/Conformance/Profiles/<Name>Profile.cs`, `TESTS/<Name>MapperTests.cs`.
- **Step 1:** read only the provider's research note, `Fixtures/<name>/SOURCE.md` and design 9.2 and 9.3.
- **Step 2:** append the `EmailProviderKind` value, a definition stub and the options class (secret `[JsonIgnore]`, `ToString()` redacted). Write `<Name>Profile` and the task's quirk tests. Run `./scripts/test.sh --filter "FullyQualifiedName~<Name>"`. Expected: FAIL (mapper not implemented).
- **Step 3:** implement the mapper (source-gen JSON types) and the validation in the definition (design 9.3).
- **Step 4:** run the same filter. Expected: PASS on both lines, every C and F scenario included. Then `--filter "FullyQualifiedName~Leak|FullyQualifiedName~Registry"`. Expected: PASS.
- **Checkpoint (Pascal commits):** `feat: <Name> provider`

### Task 17: SendGrid

- `POST /v3/mail/send`, `Authorization: Bearer`. Host from `Region` (`Global`, `EU`).
- One `personalizations[0]` holds To, Cc and Bcc. `content[]` has one item, never an empty `value`. Reply-To as `reply_to_list`. Tracking in `tracking_settings`. `MaxRequestBytes` 20 MB.
- Success 202, empty body; id from `X-Message-Id` if present, else "none". No idempotency.
- Quirk tests: `FromName_NonAscii_Dropped`, `Region_EU_UsesEuHost`, `EmptyBody_RefusedBeforeSend`, `Accepted202_NoBody_IdFromHeaderOrNone`.
- Expected: PASS. Fixtures: `request-basic`, `request-attachment`, the three error responses.

### Task 18: Resend

- `POST /emails`, `Authorization: Bearer`. `Idempotency-Key` header (the send's GUID).
- `reply_to` array, `headers` object, `attachments[].content` base64. Tracking: none per request (account setting). `MaxRequestBytes` 40 MB.
- Quota 429s (`daily_quota_exceeded`, `monthly_quota_exceeded`) are quota stops; `rate_limit_exceeded` is retryable.
- Quirk tests: `Quota429_NotRetried`, `RateLimit429_Retried`, `IdempotencyKey_OnEveryAttempt`, `Forbidden403_UnverifiedDomainHint`.
- Expected: PASS. Fixtures include `.synthesised` error bodies: trait them.

### Task 19: Mailgun

- `POST /v3/{Domain}/messages`, Basic `api:<ApiKey>`, `multipart/form-data`. Host from `Region` (`US`, `EU`). `Domain` is a required option, validated as a bare host and escaped as a path segment.
- One `to`, `cc`, `bcc` part per address. `h:Reply-To` (comma-joined), `h:<Name>` per header, `o:tracking`, `o:tracking-clicks`, `o:tracking-opens` = `no` when the switch is on. Attachments as file parts with filename and content type. `MaxRequestBytes` 25 MB.
- 420 is retryable like 429. Success 200, id from `id`.
- `Normalise` in the profile parses the multipart body (the fixtures are curl commands: compare part names, not JSON paths).
- Quirk tests: `Multipart_PartsPerRecipient`, `Status420_Retried`, `Domain_WithSlash_FailsValidation`, `Region_EU_UsesEuHost`, `BasicAuth_UsernameIsApi`.
- Expected: PASS. Error fixtures are missing: F1 and F2 serve empty bodies (status-only mapping).

### Task 20: Brevo

- `POST /v3/smtp/email`, header `api-key`. `sender{email,name}`, `to[]`, `cc[]`, `bcc[]` objects, single `replyTo`, `htmlContent` or `textContent`, `attachment[]{name, content}`.
- Header policy: drop all (`Expires` included). Tracking: account-level. Idempotency: `headers.idempotencyKey` in the body. `MaxRequestBytes`: none (package cap).
- A `duplicate_parameter` error on a retry is accepted with id "none" and a Warning (design 9.2).
- Success 201, id from `messageId`.
- Quirk tests: `CustomHeaders_Dropped_IdempotencyKeyKept`, `DuplicateOnRetry_TreatedAsAccepted`, `DuplicateOnFirstAttempt_IsFailure`, `ReplyTo_FirstOnly_WarnsCount`.
- Expected: PASS.

### Task 21: Mailjet

- `POST /v3.1/send`, Basic `<ApiKey>:<SecretKey>`. Both are secrets for the leak sweep.
- One message in `Messages[]`: `From{Email,Name}`, `To[]`, `Cc[]`, `Bcc[]`, single `ReplyTo`, `HTMLPart` or `TextPart`, `Headers`, `Attachments[]{ContentType,Filename,Base64Content}`, `TrackOpens` and `TrackClicks` = `disabled`. `MaxRequestBytes` 15 MB; at most 50 attachments.
- Fail-closed 2xx: any `Messages[].Status` not `success` is a failure with the allow-listed `ErrorCode` of the first error. Global errors (`response-error-global.json`) have no `Messages` wrapper.
- Quirk tests: `TwoXx_StatusError_IsFailure`, `GlobalError_Mapped`, `SecretKey_InLeakSweep`, `ForbiddenSender_send0008_Hint`.
- Expected: PASS.

**M7 stop.**
- [ ] Full suite: `./scripts/test.sh`. Expected: `Passed!` twice.
- [ ] Dispatch `qa-reviewer` (model `sonnet`, its frontmatter tier) with `git diff <M6 milestone commit>..HEAD`, the touched files and design 9.2. Fix Important and above.
- [ ] "Paused here" naming Task 22, and the "Milestone commit" block. Stop.

---

## M8: wave B (Sonnet)

Same recipe as M7.

### Task 22: Bird

- `POST https://{Region}.platform.bird.com/v1/email/messages`, `Authorization: Bearer`. `Region` `us1` or `eu1`. `Idempotency-Key` header.
- Always `category: "transactional"`. `track_opens` and `track_clicks` false when the switch is on (they default to true). `reply_to` array. At most 20 attachments; `MaxRequestBytes` 20 MB. The request schema rejects unknown fields, so send none.
- Start-up Warning when the key prefix names the other region. 421 maps to "Bird answered 421: the key's region does not match EmailSender:Bird:Region."
- Success 202, id from `id`.
- Quirk tests: `Category_AlwaysTransactional`, `Tracking_FalseSentExplicitly`, `Status421_RegionHint`, `KeyPrefixOtherRegion_LogsWarningAtStartup`, `Attachments21_RefusedBeforeSend`.
- Expected: PASS.

### Task 23: MailerSend

- `POST /v1/email`, `Authorization: Bearer`. `from{email,name}`, `to[]`, `cc[]` (10 max), `bcc[]` (10 max), single `reply_to`, `settings.track_clicks` and `track_opens`. Header policy: drop all. `MaxRequestBytes` 25 MB.
- Success: 202 **with** `x-message-id` (header only). 202 with no `x-message-id` is a failure (all suppressed). `x-send-paused: true` is accepted with a Warning. Fail-closed.
- `MS42901` 429 is a quota stop; `MS42903` 429 retries. `MS42205` gets the hint "MailerSend allows one recipient per field until the account is approved."
- Quirk tests: `Accepted202_IdFromHeaderOnly`, `Accepted202_NoMessageId_IsFailure`, `SendPaused_AcceptedWithWarning`, `CcOver10_RefusedBeforeSend`, `CustomHeaders_Dropped`, `Quota429_NotRetried`.
- Expected: PASS. The success case has no fixture body (202 with an empty body is documented).

### Task 24: SMTP2GO

- `POST /v3/email/send`, header `X-Smtp2go-Api-Key`. Host from `Region` (`Global`, `US`, `EU`, `AU`).
- `sender`, `to[]`, `cc[]`, `bcc[]` as strings, `html_body` or `text_body`, `custom_headers[]{header,value}` with `Reply-To` there (no field). Attachments `{filename, fileblob, mimetype}`. Never send `fastaccept`. Tracking: per API key. `MaxRequestBytes` 50 MB.
- Fail-closed 2xx: `data.failed` > 0 or `data.error_code` present is a failure. Parse both documented error shapes (nested `data` and flat). Id from `data.email_id`.
- Start-up Warning when the key does not start `api-`.
- Quirk tests: `TwoXx_FailedGreaterThanZero_IsFailure` (`response-200-with-failure.synthesised.json`), `ReplyTo_AsCustomHeader`, `FastAccept_NeverSent`, `ErrorShapes_NestedAndFlat`, `Region_EU_UsesEuHost`.
- Expected: PASS.

### Task 25: Elastic Email

- `POST /v4/emails/transactional`, header `X-ElasticEmail-ApiKey`. `Idempotency-Key` header (in its OpenAPI, design 9.13 row 17).
- `Recipients{To,CC,BCC}`, `Content{From, ReplyTo (comma string), Subject, Body[{ContentType: HTML or PlainText, Content, Charset: utf-8}], Headers, Attachments[{BinaryContent, Name, ContentType}]}`, `Options{TrackOpens, TrackClicks}`. `MaxRequestBytes` 20 MB.
- Error bodies are undocumented: status-only mapping. Id from `MessageID`.
- Quirk tests: `Body_PartTypeHtmlOrPlainText`, `ReplyTo_CommaString`, `ErrorBody_Ignored_StatusOnly`, `IdempotencyKey_Header`.
- Expected: PASS. Only `request-basic`, a synthesised attachment request and `response-success` exist; F1 and F2 serve empty bodies.

### Task 26: Mandrill

- `POST https://mandrillapp.com/api/1.4/messages/send`, key in the body (`key`). No auth header. The key is the leak sweep's highest risk: assert it never appears in logs, exceptions or `ToString()` of any request type.
- `message{from_email, from_name, to[{email,type: to|cc|bcc}], subject, html or text, headers (Reply-To there), attachments[{type,name,content}], track_opens, track_clicks}`. Never `images[]`. `MaxRequestBytes` 10 MB.
- Fail-closed 2xx: any entry `rejected` or `invalid` fails with "Mandrill rejected N of M recipients (reject_reason: <enum>)". `reject_reason` comes from the documented enum only. Errors match on `name` (`Invalid_Key`, `PaymentRequired`), never the numeric `code`.
- Quirk tests: `PerRecipientArray_AnyRejected_IsFailure`, `QueuedAndScheduled_AreAccepted`, `Unsigned_UnverifiedDomainHint`, `KeyInBody_NeverLogged`, `RecipientTypes_ToCcBcc`.
- Expected: PASS.

### Task 27: Mailtrap

- `POST https://send.api.mailtrap.io/api/send`, `Authorization: Bearer`. User-Agent is required (the shared client sets it).
- `from{email,name}`, `to[]`, `cc[]`, `bcc[]` objects, single `reply_to`, `html` or `text`, `headers`, `attachments[{content, filename, type, disposition: attachment}]`. Tracking: not per request. `MaxRequestBytes` 10 MB. Never the batch or sandbox hosts.
- `success: false` on a 200 is a failure (defensive). A 401 gets the hint "Mailtrap answers 401 for a bad token and for an unverified sending domain."
- Quirk tests: `TwoXx_SuccessFalse_IsFailure`, `Unauthorized_TokenOrDomainHint`, `Ids_PerRecipient_FirstLogged`.
- Expected: PASS.

**M8 stop.**
- [ ] Full suite: `./scripts/test.sh`. Expected: `Passed!` twice.
- [ ] Dispatch `qa-reviewer` (model `sonnet`, its frontmatter tier) with `git diff <M7 milestone commit>..HEAD`, the touched files and design 9.2. Fix Important and above.
- [ ] "Paused here" naming Task 28, and the "Milestone commit" block. Stop.

---

## M9: Microsoft Graph (Opus)

### Task 28: Graph token source

**Files:** `SRC/Providers/MicrosoftGraph/{GraphTokenSource,GraphTokenResponse}.cs`, `SRC/Configuration/MicrosoftGraphOptions.cs`, `TESTS/GraphTokenSourceTests.cs`

**Interfaces:** Produces `internal sealed class GraphTokenSource { ValueTask<string> GetAsync(CancellationToken ct); void Invalidate(); }`, a singleton over the named client `community.emailSender.graphToken`, `IOptionsMonitor` and `TimeProvider`.

- [ ] **Step 1:** write the failing tests with `FakeTimeProvider` and `Recorder`, serving `Fixtures/graph/token-response.json` and `token-error.json`:
  - `Request_IsFormWithClientCredentials` (shape match with `token-request.txt`; the secret is form-encoded);
  - `Cached_UntilFiveMinutesBeforeExpiry`, `ShortLifetime_UsesSixtySecondMinimum`;
  - `ConcurrentSends_OneTokenRequest` (20 parallel callers, 1 request);
  - `Invalidate_ForcesNewToken`, `OptionsChange_DropsToken`;
  - `TokenError_EchoesAllowListedCodeOnly` (`invalid_scope` kept; `error_description` never in the message);
  - `Token_NeverInLogsOrExceptions` (sentinel token);
  - `TenantId_PathEscaped_AndValidated`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~GraphTokenSource"`. Expected: FAIL.
- [ ] **Step 3:** implement design 9.4 (token cache). The token is opaque: no JWT parsing.
- [ ] **Step 4:** run the same filter. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `feat: Microsoft Graph token source`

### Task 29: Graph mapper, profile and health check

**Files:** `SRC/Providers/MicrosoftGraph/{GraphMapper,GraphRequest}.cs`, the definition and `EmailProviderKind.MicrosoftGraph`, `TESTS/Conformance/Profiles/MicrosoftGraphProfile.cs`, `TESTS/GraphMapperTests.cs`

- `POST https://graph.microsoft.com/v1.0/users/{SenderUserId}/sendMail` with the token. `saveToSentItems` from options (default false).
- `message{subject, body{contentType: HTML or Text, content}, toRecipients, ccRecipients, bccRecipients, replyTo, internetMessageHeaders (x- names only), attachments[#microsoft.graph.fileAttachment]}`. `UsesFromAddress = false`. At most 3 MB per attachment.
- Any 2xx is accepted, id "none". A 401 invalidates the token and retries once. 429 honours `Retry-After`. No idempotency.
- Health check: token request only (design 9.6 text).

- [ ] **Step 1:** write `MicrosoftGraphProfile` and the quirk tests: `Accepts202And204`, `Unauthorized_RefreshesOnceThenFails`, `Headers_XPrefixOnly_ExpiresDropped`, `MessageFrom_IgnoredWithDebug`, `AttachmentOver3Mb_RefusedBeforeSend`, `SenderUserId_PathEscaped`, `HealthCheck_TokenOnly_Text`. Fixtures: `request-basic`, `request-attachment`, `request-headers`, `response-error-validation`, `response-ratelimit.txt`, `response-success.txt`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Graph"`. Expected: FAIL.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** run `./scripts/test.sh`. Expected: PASS on both lines, the registry guard and leak sweep included.
- [ ] **Checkpoint (Pascal commits):** `feat: Microsoft Graph provider`

**M9 stop.**
- [ ] Full suite. Expected: `Passed!` twice.
- [ ] Dispatch `security-analyst` (model `opus`) and `qa-reviewer` (model `opus`) with `git diff <M8 milestone commit>..HEAD`, the touched files, design 9.4 and 9.9 point 5, and Review Focus 9. Fix Important and above. A Critical: stop and ask Pascal.
- [ ] "Paused here" naming Task 30, and the "Milestone commit" block. Stop.

---

## M10: guides, final review, CI, live checks and the alpha (Sonnet; reviewers on Opus)

### Task 30: README provider guides, status table and tags

**Files:** `README.md`, `CLAUDE.md`, `SRC/Umbraco.Community.EmailSender.csproj` (`PackageTags`), `TESTS/ReadmeTests.cs`

- [ ] **Step 1:** write the failing tests: `ReadmeTests.EveryProviderHasGuideAndLabel` (a `###` heading per `EmailProviderKind` value, each with a label from the four in design 9.7), `ReadmeTests.NoSupportedClaimBeyondLabel` (every "supported" is "not supported" or inside a label sentence), `PackageTags_HaveNoMarketplaceTagWhilePrerelease`.
- [ ] **Step 2:** run `./scripts/test.sh --filter "FullyQualifiedName~Readme"`. Expected: FAIL.
- [ ] **Step 3:** write the README:
  - the status table (design 9.7 labels at the first alpha; Postmark stays "Built from the provider's docs..." until Task 11 records "Verified live");
  - one guide per provider from design 9.8 and its research note: key, config (env-var form first), verification, free tier "as of November 2026", caveats;
  - ACS: "Microsoft retires ACS Email on 30 Sep 2028"; Notify: Experimental; Graph: the RBAC-for-Applications note before its config sample;
  - the retry rules in two lines, and "core's SMTP check will fail; ignore it" (kept);
  - no residency claim where a provider's docs say nothing.
  - `CLAUDE.md`: add the pipeline, the registry guard and "unknowns carried, not chased" to the behaviours to preserve.
  - `PackageTags`: add the provider tags of design 9.12; drop `umbraco-marketplace` while the version is a prerelease.
- [ ] **Step 4:** re-run. Expected: PASS on both lines.
- [ ] **Checkpoint (Pascal commits):** `docs: provider guides and status table`

### Task 31: Final whole-branch review

- [ ] **Step 1:** dispatch `qa-reviewer` (model `opus`) and `security-analyst` (model `opus`), fresh context, with the **full** branch (not a diff), `docs/*`, the Umbraco pitfalls list and design 9.9. Ask for findings by severity against Global Constraints, both Review Focus lists and design 9.5.
- [ ] **Step 2:** keep the verdicts outside the repo. Fix Critical, High and Important findings under the owning task's tests. A Critical security finding: stop and ask Pascal. Run `./scripts/test.sh`. Expected: `Failed: 0` twice.
- [ ] **Checkpoint (Pascal commits):** `fix: final review findings (provider extension)`

### Task 32: GitHub CI run for the extension (Pascal, with the agent fixing)

- [ ] **Step 1 (agent):** the Task 9 Step 1 pre-push checks, plus `gitleaks dir src/Umbraco.Community.EmailSender.Tests/Fixtures --redact` and `grep -rnE "/Us[e]rs/|~/Do[c]uments|~/Pr[o]jects" . --exclude-dir=.git -I --exclude='*.lock.json'`. Expected: no leaks, no hits. Say plainly in the handover: **CI has not run on this code yet**.
- [ ] **Step 2 (Pascal):** push the branch and open a PR. Expected: every required check green in about 5 minutes.
- [ ] **Step 3 (Pascal):** run Release checks by hand on the branch. Expected: both lines green.
- [ ] **Step 4 (agent):** fix each failure under the owning task's tests until Steps 2 and 3 are green. Record local-versus-GitHub differences in `docs/spikes.md`.
- [ ] **Checkpoint (Pascal commits):** `ci: fixes from the extension's GitHub run` (only if anything changed).

**Then Task 11 (narrowed: Postmark only, Pascal-gated).**

### Task 33: Optional live checks (each one Pascal-gated, each one skippable)

Nothing here blocks the alpha. The session asks Pascal **once**, as one list: "Which of these do you want to run now?" It runs only the ones he names, in his order, and waits for each secret to be in user-secrets. It never asks for a value and never signs up for anything. A skipped provider keeps its label.

The same check for each: on the 18.3 demo site first, then 17.8, with no `Smtp:Host`, set `Provider` and the provider's section; invite a user (to Pascal's own address); reset that user's password; run the health check.

**Record in `docs/spikes.md` for each check:** date; provider; Umbraco versions; the account option used (below); for each email, sent yes or no and the first 8 characters of the message id (or "none"); whether `Expires` was accepted; whether the invite link arrived unchanged (tracking off); the health-check text; any surprise against the research note. No addresses, subjects or keys. A full pass moves the label to "Verified live".

| Provider | Free route for Pascal | What it proves beyond the suite | Watch for |
|---|---|---|---|
| Resend | Free plan, `onboarding@resend.dev` to his own sign-up address | Auth, User-Agent, payload, success id | 403 if the recipient is not his own address |
| Bird | Free plan (no card), `onboarding@messagebird.dev` to a verified workspace member, `Region` per his sign-up | `category`, tracking off, **whether `Expires` is reserved** | 421 means a region mismatch; 422 naming a header means drop `Expires` for Bird |
| Brevo | Free plan after account approval, validated sender | Header drop, `api-key` carriage | 402 `account_under_validation` until approved |
| Mailjet | Free plan (no card), validated sender | 2xx body parsing (`Messages[].Status`) | 403 `send-0008` if the sender is not validated |
| Mailtrap | Free plan, demo domain to his own sign-up address | Auth, User-Agent, success ids | 401 also means "unverified domain" |
| ACS | Only with a free Azure account he already has: an Azure-managed domain | SDK path, `UserEngagementTrackingDisabled`, whether `Expires` is accepted (open since M2) | The resource region decides data location; no claim either way |
| GOV.UK Notify, Microsoft Graph, SendGrid, MailerSend, SMTP2GO, Mandrill | Not planned: no free route for Pascal | | Labels stay as design 9.7 |

AI time: about 0.2 h per check Pascal picks. Pascal's time: about 0.3 h each.

- [ ] **Checkpoint (Pascal commits):** `docs: live check results` (only if any check ran).

### Task 34: Alpha release (Pascal)

- [ ] **Step 1 (agent):** check that every README label matches `docs/spikes.md` evidence (design 9.7). Postmark is "Verified live" only if Task 11 recorded a real send; otherwise the README says what the run proved. Update the labels for any Task 33 passes. Run `./scripts/test.sh`. Expected: `Failed: 0` twice.
- [ ] **Step 2 (agent):** write HANDOVER.md in full (what was built, review findings and fixes, test results with commands, labels and their evidence, unknowns carried, Pascal's to-do list).
- [ ] **Step 3 (Pascal):** run Release checks; squash-merge; tag `release/0.0-alpha.1` on the merge commit. Expected: `17.0.0-alpha.1` and `18.0.0-alpha.1` on NuGet from one commit. `gh release create --prerelease --generate-notes`.
- [ ] **Step 4 (Pascal):** no Marketplace listing (alpha). Open before stable: the Marketplace tag back in, the deferred minors in COMMITS-TODO.md, and any label upgrades.
- [ ] **Checkpoint (Pascal commits):** `docs: release notes and handover` (only if anything changed).

### Later (not planned)

- **Amazon SES:** SigV4 by hand, recipe and official test vector in the research note; about 3 to 4 h. Starts only when Pascal says go.
- Graph certificate credentials and sovereign clouds; Mailtrap sandbox host for development; Mailgun AU; inline images (needs a content-id model first).
