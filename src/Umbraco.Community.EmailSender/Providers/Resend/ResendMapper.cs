using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Resend;

/// <summary>
/// Resend's <c>POST /emails</c> (design 9.2). The one idempotency key per send goes in <c>Idempotency-Key</c> on every attempt,
/// which is what makes a retry of a 500 safe. The error <c>name</c> is the only part of the body ever echoed.
/// </summary>
internal sealed class ResendMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.resend";

    private const string Host = "https://api.resend.com/";

    public string Name => "Resend";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["api.resend.com"],
        // Per email after base64 [docs].
        MaxRequestBytes = 40 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.List,
        // A per-domain setting, off by default; the README says where.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.Header,
        Inline = InlineContentId.ContentId,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var body = new ResendRequest(
            email.From,
            email.To,
            NullIfEmpty(email.Cc),
            NullIfEmpty(email.Bcc),
            NullIfEmpty(email.ReplyTo),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            email.Headers.Count == 0 ? null : email.Headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new ResendAttachment(a.Name, a.Base64, a.ContentType))]);

        var request = new HttpRequestMessage(HttpMethod.Post, Host + "emails")
        {
            Content = JsonContent.Create(body, ResendJsonContext.Default.ResendRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Resend.ApiKey);
        request.Headers.Add("Idempotency-Key", attempt.IdempotencyKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (response.IsSuccessStatusCode)
        {
            if (json is null)
            {
                return ProviderOutcome.Unreadable;
            }

            return ResponseReader.String(json.RootElement, "id") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
        }

        var code = json is null ? null : ResponseReader.String(json.RootElement, "name");
        // A daily or monthly quota does not clear by waiting; a per-second rate limit does (design 9.2).
        var quota = response.StatusCode == HttpStatusCode.TooManyRequests && code is "daily_quota_exceeded" or "monthly_quota_exceeded";
        return ProviderOutcome.ForStatus(response.StatusCode, code, Hint(response.StatusCode, code), quota);
    }

    private static string? Hint(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.Unauthorized, _) or (HttpStatusCode.Forbidden, "restricted_api_key" or "suspended_api_key") => "Check EmailSender:Resend:ApiKey.",
        (HttpStatusCode.Forbidden, _) => "Verify the sender domain in Resend. Without a verified domain Resend delivers only to the account owner's own address.",
        _ => null,
    };

    private static List<string>? NullIfEmpty(IReadOnlyList<string> values) => values.Count == 0 ? null : [.. values];
}
