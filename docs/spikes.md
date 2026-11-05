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
