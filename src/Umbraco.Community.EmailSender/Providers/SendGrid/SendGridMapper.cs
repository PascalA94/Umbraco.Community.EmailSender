using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.SendGrid;

/// <summary>
/// SendGrid's <c>POST /v3/mail/send</c> (design 9.2 and 9.3). Success is 202 with an empty body; the id, when SendGrid sends one,
/// is the <c>X-Message-Id</c> header. SendGrid documents no idempotency key, so only not-accepted signals are retried.
/// </summary>
internal sealed class SendGridMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.sendGrid";

    internal const string GlobalHost = "api.sendgrid.com";
    internal const string EuHost = "api.eu.sendgrid.com";
    internal static readonly string[] Regions = ["Global", "EU"];

    public string Name => "SendGrid";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = [GlobalHost, EuHost],
        // The docs say 20 MB in one place and 30 MB in another; the lower is used.
        MaxRequestBytes = 20 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.List,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var content = new List<SendGridContent>();
        if (!string.IsNullOrEmpty(email.TextBody))
        {
            content.Add(new("text/plain", email.TextBody));
        }

        if (!string.IsNullOrEmpty(email.HtmlBody))
        {
            content.Add(new("text/html", email.HtmlBody));
        }

        // SendGrid refuses an empty content value, so say so here rather than send a request that can only fail.
        if (content.Count == 0)
        {
            throw new EmailSendException("The email has no body, and SendGrid refuses an empty one.");
        }

        var body = new SendGridRequest(
            [new([.. email.To.Select(a => new SendGridAddress(a))], Addresses(email.Cc), Addresses(email.Bcc))],
            // "Unicode encoding is not supported for the from field" [docs]: a non-ASCII display name is dropped, the address stays.
            new SendGridAddress(email.FromAddress, email.FromName is { } name && name.All(c => c < 128) ? name : null),
            Addresses(email.ReplyTo),
            email.Subject,
            content,
            email.Headers.Count == 0 ? null : email.Headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new SendGridAttachment(a.Base64, a.Name, a.ContentType, "attachment"))],
            options.DisableLinkTracking ? new SendGridTracking(new(false, false), new(false)) : null);

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host(options)}/v3/mail/send")
        {
            Content = JsonContent.Create(body, SendGridJsonContext.Default.SendGridRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SendGrid.ApiKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        if (response.IsSuccessStatusCode)
        {
            // 202 has no body (200 only in sandbox mode). The id header is undocumented, so its absence is normal.
            return response.Headers.TryGetValues("X-Message-Id", out var ids) && ids.FirstOrDefault() is { } id
                ? ProviderOutcome.Accepted([id])
                : ProviderOutcome.Accepted([]);
        }

        // errors[].message can echo addresses, so the body is never read: status and a fixed hint only.
        return ProviderOutcome.ForStatus(response.StatusCode, null, Hint(response.StatusCode));
    }

    private static string? Hint(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Check EmailSender:SendGrid:ApiKey.",
        HttpStatusCode.Forbidden => "Verify the sender or domain in SendGrid, and check the API key has Mail Send permission.",
        HttpStatusCode.RequestEntityTooLarge => "The email is too large for SendGrid.",
        _ => null,
    };

    private static string Host(EmailSenderOptions options) => ProviderValidation.MatchRegion(options.SendGrid.Region, Regions) switch
    {
        "Global" => GlobalHost,
        "EU" => EuHost,
        _ => throw new EmailSendException($"EmailSender:SendGrid:Region must be one of: {string.Join(", ", Regions)}."),
    };

    private static List<SendGridAddress>? Addresses(IReadOnlyList<string> values)
        => values.Count == 0 ? null : [.. values.Select(a => new SendGridAddress(a))];
}
