# Email Sender

Send every Umbraco email through an HTTP API (Azure Communication Services, Postmark, GOV.UK Notify) instead of SMTP.

[![Build](https://github.com/PascalA94/Umbraco.Community.EmailSender/actions/workflows/build.yml/badge.svg)](https://github.com/PascalA94/Umbraco.Community.EmailSender/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.EmailSender)](https://www.nuget.org/packages/Umbraco.Community.EmailSender)
[![NuGet downloads](https://img.shields.io/nuget/dt/Umbraco.Community.EmailSender)](https://www.nuget.org/packages/Umbraco.Community.EmailSender)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue)](https://github.com/PascalA94/Umbraco.Community.EmailSender/blob/main/LICENSE)

A community package, not made or supported by Umbraco HQ. Requires Umbraco 17 (17.8.0 or later) or Umbraco 18 (18.3.0 or later), on .NET 10.

## Compatibility

| Package version | Umbraco |
|---|---|
| 17.x | 17 (17.8.0 or later) |
| 18.x | 18 (18.3.0 or later) |

## Install

```bash
dotnet add package Umbraco.Community.EmailSender
```

## Quick start

Set the provider and its secret. Nothing else changes: password reset, user invites, content notifications and health-check emails all go through the API.

```json
{
  "EmailSender": {
    "Provider": "Postmark",
    "From": "noreply@example.com"
  }
}
```

Put the secret in an environment variable or a secret store, never in a committed `appsettings.json`:

```bash
EmailSender__Postmark__ServerToken=<your server token>
```

If `EmailSender:Provider` is not set, the package does nothing and Umbraco's SMTP settings stay in charge. A site with no `Umbraco:CMS:Global:Smtp:Host` still offers user invites and password reset once a provider is set.

## Settings

| Key | Default | Meaning |
|---|---|---|
| `EmailSender:Provider` | unset | `AzureCommunicationServices`, `Postmark` or `GovUkNotify` (case-insensitive). Unset means this package is inactive. |
| `EmailSender:From` | unset | Used when a message has no From. Then `Umbraco:CMS:Global:Smtp:From` is tried. Not used by GOV.UK Notify. |
| `EmailSender:DisableLinkTracking` | `true` | Asks the provider not to rewrite links, so mail scanners do not consume invite links. |
| `EmailSender:TimeoutSeconds` | `30` | How long one provider call may take, 1 to 300. |
| `EmailSender:MaxTotalAttachmentBytes` | `10485760` | Largest total attachment size (raw bytes) before nothing is sent. Base64 adds about a third on the wire. |
| `EmailSender:Postmark:ServerToken` | none | **Secret.** A Postmark *server* token, not the account token. |
| `EmailSender:Postmark:MessageStream` | `outbound` | The message stream to send on. |
| `EmailSender:AzureCommunicationServices:ConnectionString` | none | **Secret.** `endpoint=https://<resource>.communication.azure.com/;accesskey=<key>`. |
| `EmailSender:GovUkNotify:ApiKey` | none | **Secret.** The full Notify API key. |
| `EmailSender:GovUkNotify:EmailTemplateId` | none | The id of your one email template (see below). |
| `EmailSender:GovUkNotify:ReplyToId` | unset | The id of a reply-to address registered in Notify. |

A wrong or missing setting stops the site at start-up with a message that names the key, never the value. These keys are permanent: they will not be renamed within a major version.

A change to `Provider` needs a restart, because it decides which services are registered. Other settings are read on every send.

## Providers

### Postmark

Create a server, use its **Server API token**, and give the sending domain a verified signature. With `DisableLinkTracking` on, each message is sent with `TrackLinks` set to `None` and `TrackOpens` off. Rotate a token in the server's API Tokens tab.

### Azure Communication Services

Create an Email Communication Services resource with a verified domain, connect it to a Communication Services resource, and use that resource's connection string. Use a resource that only this site uses, so a leak is contained. The sender address (`EmailSender:From` or the Smtp From) must be a MailFrom address on the verified domain. A display name in the From setting is dropped.

- The endpoint must be `https` on a `*.communication.azure.com`, `.us` or `.cn` host. Another host is refused at send time, and there is no setting to override it.
- With `DisableLinkTracking` on, each message asks the service not to track user engagement. Also turn off user engagement tracking on the domain: the domain setting applies to any message that does not carry the switch.
- The package does not wait for delivery. It stops once Azure has accepted the request, and logs the operation id.
- Retries are off, because a retry after a lost answer could send an invite twice.

### GOV.UK Notify

Notify sends from templates, so the package uses **one email template** for everything:

- Subject: `((subject))`
- Body: `((body))`

Put the template's id in `EmailSender:GovUkNotify:EmailTemplateId`. The health check warns when the template does not look like this.

What to expect:

- Notify shows HTML as text, so an HTML message is reduced to plain text. Links to `http` and `https` addresses stay as links. Layout and images are lost.
- Notify sends from the address set up in your Notify service. `EmailSender:From` is not used. A reply-to address in a message is ignored (with a warning in the log); set `ReplyToId` to use one registered in Notify.
- One request goes out per recipient in `To`, up to 50. If a later one fails, the error says how many were sent.
- **Attachments, Cc and Bcc are not supported** and fail with a clear message before anything is sent.
- A team-only key, or a service in trial mode, can only send to team members. The error says so.
- Notify does not track links.
- Square brackets in message text are shown as round brackets, so text from a content editor or a form cannot form a Markdown link in an email that comes from your Notify sender.
- A retry after a partial failure sends to the earlier recipients again.
- Use a dedicated **live** key for this site, kept in an environment variable or secret store. Revoke and replace it under API integration, API keys in Notify. Notify shows recent notifications, personalisation included, to your Notify team, and an invite or reset link is a credential: keep the team small and Umbraco's token lifetimes short.

## Health check

A check named "Email provider" appears under Services in the Health Check dashboard. It confirms that the provider accepts the credentials without sending an email: Postmark and GOV.UK Notify each make an authenticated read, and Azure only proves that the endpoint answers over TLS (it cannot prove sending works without sending).

Umbraco's own "SMTP Settings" health check still tests SMTP and will fail on a site with no SMTP host. Ignore it.

## Security notes

- Secrets are read from configuration only. They are never stored in the database, and never appear in logs, exception text, health-check text or `ToString()`.
- Recipient addresses and subjects are never logged. Logs carry the provider name, the recipient count, the attachment count, the provider's message id and the elapsed time.
- Error messages carry the HTTP status and the provider's error code or class name. The provider's response text is never copied, because an API can echo what it was sent.
- HTTP clients follow no redirects, so a credential is never sent to another host.
- Invite and reset links are bearer secrets. Keep `DisableLinkTracking` on so a provider does not rewrite or log them.
- Do not add a host-wide resilience or retry handler (for example `ConfigureHttpClientDefaults` with `AddStandardResilienceHandler`). It would retry a POST that may already have been accepted and send an email twice.
- A message reply-to is not sent by Notify (see above), so replies go to the reply-to registered in Notify.
- Do not turn on host-wide HTTP body logging for the `community.emailSender.*` clients: request bodies hold the recipient and the message. This package removes the factory's own logging from its clients because that logs credential headers at Trace.
- Report a vulnerability as described in `SECURITY.md`.

## Limits

- No templates, queues, back-off retries or bounce handling.
- Member-facing and marketing email is out of scope. Anything that calls Umbraco's `IEmailSender` is covered.
- Attachments are read into memory before sending, up to `MaxTotalAttachmentBytes`.

## Uninstall

Remove the package (or just the `EmailSender:Provider` setting). Umbraco's SMTP settings take over again at the next start. Nothing is written to the database.

## Develop

See `.github/CONTRIBUTING.md` and `CLAUDE.md` for the commands. In short: `./scripts/test.sh` builds and tests both Umbraco lines (17 and 18) from this one branch; run the demo site under `demo/`.

## Licence

MIT. See `LICENSE`. Security issues: see `SECURITY.md`.
