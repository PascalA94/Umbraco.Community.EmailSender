using System.Net;
using System.Net.Http.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.ElasticEmail;

/// <summary>
/// Elastic Email's <c>POST /v4/emails/transactional</c> (design 9.2 and 9.3). Its error bodies are undocumented, so a failure is
/// status and a fixed hint only: no part of the body is read. The idempotency key goes in <c>Idempotency-Key</c> on every
/// attempt, which is what makes a retry of a 500 safe.
/// </summary>
internal sealed class ElasticEmailMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.elasticemail";

    public string Name => "Elastic Email";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["api.elasticemail.com"],
        // The vendor's skill file says 20 MB for message and attachments [secondary source]; it is not in the OpenAPI.
        MaxRequestBytes = 20 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        // One comma-separated string.
        ReplyTo = ReplyToStyle.CommaSeparated,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.Header,
        // The schema has no content-id field.
        Inline = InlineContentId.None,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var parts = new List<ElasticEmailBodyPart>();
        if (email.HtmlBody is not null)
        {
            parts.Add(new ElasticEmailBodyPart("HTML", email.HtmlBody, "utf-8"));
        }

        if (email.TextBody is not null)
        {
            parts.Add(new ElasticEmailBodyPart("PlainText", email.TextBody, "utf-8"));
        }

        var body = new ElasticEmailRequest(
            new ElasticEmailRecipients(email.To, email.Cc.Count == 0 ? null : [.. email.Cc], email.Bcc.Count == 0 ? null : [.. email.Bcc]),
            new ElasticEmailContent(
                email.From,
                email.ReplyTo.Count == 0 ? null : string.Join(",", email.ReplyTo),
                email.Subject,
                parts,
                email.Headers.Count == 0 ? null : email.Headers,
                email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new ElasticEmailAttachment(a.Base64, a.Name, a.ContentType))]),
            options.DisableLinkTracking ? new ElasticEmailOptionsBody(TrackOpens: false, TrackClicks: false) : null);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.elasticemail.com/v4/emails/transactional")
        {
            Content = JsonContent.Create(body, ElasticEmailJsonContext.Default.ElasticEmailRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Add("X-ElasticEmail-ApiKey", options.ElasticEmail.ApiKey);
        request.Headers.Add("Idempotency-Key", attempt.IdempotencyKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Error bodies are undocumented [unknown], so none of the text is read or echoed.
            return ProviderOutcome.ForStatus(response.StatusCode, null, Hint(response.StatusCode));
        }

        // A 200 means accepted only: a later suppression shows up by status call or webhook, not here.
        using var json = ResponseReader.TryParse(body);
        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        return ResponseReader.String(json.RootElement, "MessageID") is { Length: > 0 } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
    }

    private static string? Hint(HttpStatusCode status) => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        ? "Check EmailSender:ElasticEmail:ApiKey."
        : null;
}
