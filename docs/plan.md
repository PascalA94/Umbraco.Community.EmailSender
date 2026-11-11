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

- [ ] **Step 1 (Pascal):** a Postmark test server (or the `POSTMARK_API_TEST` token) and an ACS test resource with a verified sender domain. Secrets go in `dotnet user-secrets` on the demo site, never in chat or the repo.
- [ ] **Step 2:** on **both** demo sites (17.8 and 18.3), with no `Smtp:Host` and `Provider: Postmark`:
  - invite a user, reset a password, send a content notification, and run the health-check email;
  - add a Forms "send email" workflow if Forms is installed (records the [unverified] Forms path in `docs/spikes.md`).
  - Expected: each arrives (or shows in the Postmark activity log). Invite links aren't rewritten.
- [ ] **Step 3:** repeat Step 2 on both sites with `Provider: AzureCommunicationServices`.
- [ ] **Step 4:** negatives:
  - a wrong token gives the 401 text and a health check Error;
  - a 12 MB attachment through a test controller in the demo site only gives the size error;
  - remove `Provider`, and core SMTP behaviour is back.
- [ ] **Step 5:** `grep -rciE "pm-|endpoint=|@example\.test|<subject text>" demo/**/umbraco/Logs` prints only zeros.
- [ ] **Step 6 (Pascal):** run `release-checks.yml`, then push the tag `release/0.0-alpha.1`. Expected: `17.0.0-alpha.1` and `18.0.0-alpha.1` on NuGet from one commit. Run `gh release create --prerelease --generate-notes`. The Marketplace listing goes on at `17.0.0` and `18.0.0`.
- [ ] **Checkpoint (Pascal commits):** `docs: acceptance results`

## Scope change (Pascal, 2026-11-04)

- **Add GOV.UK Notify as a provider.** See the scope.md section "Added provider". The plan needs a Notify provider task (template-based, `((subject))`/`((body))`, no attachments) before the final "exercise it for real" task. Use a Notify trial-mode key in the test.
