using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Mailgun;

/// <summary>
/// Mailgun's <c>POST /v3/{domain}/messages</c>, a <c>multipart/form-data</c> body (design 9.2 and 9.3). 420 is a rate limit
/// like 429. Mailgun documents no idempotency key and no error body shape, so failures are status and a fixed hint only.
/// </summary>
internal sealed class MailgunMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.mailgun";

    internal const string UsHost = "api.mailgun.net";
    internal const string EuHost = "api.eu.mailgun.net";
    internal static readonly string[] Regions = ["US", "EU"];

    public string Name => "Mailgun";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = [UsHost, EuHost],
        MaxRequestBytes = 25 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        // No dedicated field: Reply-To is the standard header sent as an h: part.
        ReplyTo = ReplyToStyle.FormPart,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.FileNameOnly,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var domain = options.Mailgun.Domain;
        // Start-up validation already ran, but options can reload: the domain is a path segment, so it is checked again.
        if (domain is null || !MailgunValidation.IsBareHost(domain))
        {
            throw new EmailSendException(MailgunValidation.DomainMessage);
        }

        var form = new MultipartFormDataContent();
        Add(form, "from", email.From);
        foreach (var to in email.To)
        {
            Add(form, "to", to);
        }

        foreach (var cc in email.Cc)
        {
            Add(form, "cc", cc);
        }

        foreach (var bcc in email.Bcc)
        {
            Add(form, "bcc", bcc);
        }

        Add(form, "subject", email.Subject);
        if (email.TextBody is not null)
        {
            Add(form, "text", email.TextBody);
        }

        if (email.HtmlBody is not null)
        {
            Add(form, "html", email.HtmlBody);
        }

        if (email.ReplyTo.Count > 0)
        {
            Add(form, "h:Reply-To", string.Join(", ", email.ReplyTo));
        }

        foreach (var header in email.Headers)
        {
            Add(form, "h:" + header.Key, header.Value);
        }

        if (options.DisableLinkTracking)
        {
            Add(form, "o:tracking", "no");
            Add(form, "o:tracking-clicks", "no");
            Add(form, "o:tracking-opens", "no");
        }

        foreach (var attachment in email.Attachments)
        {
            var file = new ByteArrayContent(Convert.FromBase64String(attachment.Base64));
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
            // A normal attachment, never `inline`: Umbraco's EmailMessage has no content id (design 9.2).
            form.Add(file, "attachment", attachment.Name);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host(options)}/v3/{Uri.EscapeDataString(domain)}/messages") { Content = form };
        // Built from parts and never logged: the username is the literal "api" and the password is the key.
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("api:" + options.Mailgun.ApiKey)));
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Error bodies are undocumented and carry a free-text message: status and a fixed hint only.
            return ProviderOutcome.ForStatus(response.StatusCode, null, Hint(response.StatusCode));
        }

        // "Queued" means accepted; delivery failures arrive later as events, never in this response.
        using var json = ResponseReader.TryParse(body);
        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        return ResponseReader.String(json.RootElement, "id") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
    }

    private static string? Hint(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Check EmailSender:Mailgun:ApiKey, and that EmailSender:Mailgun:Region matches the domain's region.",
        HttpStatusCode.Forbidden => "Check EmailSender:Mailgun:Domain and the API key's scope. A sandbox domain sends only to authorised recipients.",
        _ => null,
    };

    private static string Host(EmailSenderOptions options) => ProviderValidation.MatchRegion(options.Mailgun.Region, Regions) switch
    {
        "US" => UsHost,
        "EU" => EuHost,
        _ => throw new EmailSendException($"EmailSender:Mailgun:Region must be one of: {string.Join(", ", Regions)}."),
    };

    private static void Add(MultipartFormDataContent form, string name, string value)
    {
        var part = new StringContent(value, Encoding.UTF8);
        // A plain form field has no content type of its own.
        part.Headers.ContentType = null;
        form.Add(part, name);
    }
}
