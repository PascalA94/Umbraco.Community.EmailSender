using System.Net;
using System.Net.Http.Json;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

/// <summary>
/// Postmark's <c>POST /email</c>. Failures carry the HTTP status and Postmark's numeric ErrorCode only: never the response
/// text. Postmark documents no idempotency key, so only not-accepted signals are retried.
/// </summary>
internal sealed class PostmarkMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.postmark";

    private const string Host = "https://api.postmarkapp.com/";
    private const string TokenHeader = "X-Postmark-Server-Token";

    public string Name => "Postmark";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        // 10 MB for the whole request [docs]; the default 10 MB raw attachment cap exceeds this after base64 (design 9.13 row 7).
        MaxRequestBytes = 10 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.CommaSeparated,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        ErrorCodeLabel = "ErrorCode",
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var body = new PostmarkRequest(
            email.From,
            string.Join(',', email.To),
            Join(email.Cc),
            Join(email.Bcc),
            Join(email.ReplyTo),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            options.Postmark.MessageStream,
            options.DisableLinkTracking ? false : null,
            options.DisableLinkTracking ? "None" : null,
            email.Headers.Count == 0 ? null : [.. email.Headers.Select(h => new PostmarkHeader(h.Key, h.Value))],
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new PostmarkAttachment(a.Name, a.Base64, a.ContentType))]);

        var request = new HttpRequestMessage(HttpMethod.Post, Host + "email")
        {
            Content = JsonContent.Create(body, PostmarkJsonContext.Default.PostmarkRequest),
        };
        AddToken(request, options);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        var code = json is null ? null : ResponseReader.Int(json.RootElement, "ErrorCode");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return ProviderOutcome.ForStatus(response.StatusCode, code?.ToString(System.Globalization.CultureInfo.InvariantCulture), Hint(response.StatusCode, code));
        }

        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        // The single send documents non-200 on failure; a non-zero ErrorCode in a 200 is still not a send (design 9.2).
        if (code is { } failed && failed != 0)
        {
            return ProviderOutcome.Rejected(200, failed.ToString(System.Globalization.CultureInfo.InvariantCulture), Hint(response.StatusCode, code), retryable: false, quotaStop: false);
        }

        return ResponseReader.String(json.RootElement, "MessageID") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
    }

    public HttpRequestMessage? BuildHealthRequest(EmailSenderOptions options)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Host + "server");
        AddToken(request, options);
        return request;
    }

    public HealthCheckStatus ReadHealth(HttpResponseMessage response, ReadOnlyMemory<byte> body) => response.StatusCode switch
    {
        HttpStatusCode.OK => new HealthCheckStatus("Postmark accepted the server token.") { ResultType = StatusResultType.Success },
        HttpStatusCode.Unauthorized => new HealthCheckStatus("Postmark rejected the server token.") { ResultType = StatusResultType.Error },
        _ => new HealthCheckStatus($"Postmark answered HTTP {(int)response.StatusCode}.") { ResultType = StatusResultType.Error },
    };

    // Fixed text per documented code [docs: Postmark API error codes]. The unconfirmed-signature code is undocumented
    // (docs/spikes.md, M6), so that 422 shows status and code only.
    private static string? Hint(HttpStatusCode status, int? code) => (status, code) switch
    {
        (HttpStatusCode.Unauthorized, _) => "Check EmailSender:Postmark:ServerToken.",
        (_, 412) => "While the Postmark account is pending approval, every recipient must share the From address's domain.",
        (_, 413) => "The Postmark account is not approved to send email yet.",
        _ => null,
    };

    // A header value with CR or LF throws FormatException here; the provider wraps it by type name, so the token is never echoed.
    private static void AddToken(HttpRequestMessage request, EmailSenderOptions options)
        => request.Headers.Add(TokenHeader, options.Postmark.ServerToken);

    private static string? Join(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join(',', values);
}
