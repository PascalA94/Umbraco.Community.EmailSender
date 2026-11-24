using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.MailerSend;

/// <summary>
/// MailerSend's <c>POST /v1/email</c> (design 9.2 and 9.3). Success is a 202 with an empty body and the id in the
/// <c>x-message-id</c> header; a 202 without that header sent nothing (every recipient suppressed). Error codes are read from
/// the <c>#MSnnnnn</c> suffix of the message text, and nothing else of the message is ever echoed.
/// </summary>
internal sealed partial class MailerSendMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.mailersend";

    public string Name => "MailerSend";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["api.mailersend.com"],
        MaxCc = 10,
        MaxBcc = 10,
        MaxRequestBytes = 25 * 1024 * 1024,
        // Custom headers need a Professional plan (422 MS42233 otherwise), so none is sent.
        Headers = HeaderPolicy.DropAll,
        ReplyTo = ReplyToStyle.Single,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        // A failure can hide in a 202 that carries no x-message-id.
        FailClosedOn2xx = true,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var body = new MailerSendRequest(
            new MailerSendAddress(email.FromAddress, email.FromName),
            [.. email.To.Select(a => new MailerSendAddress(a))],
            email.Cc.Count == 0 ? null : [.. email.Cc.Select(a => new MailerSendAddress(a))],
            email.Bcc.Count == 0 ? null : [.. email.Bcc.Select(a => new MailerSendAddress(a))],
            email.ReplyTo.Count == 0 ? null : new MailerSendAddress(email.ReplyTo[0]),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new MailerSendAttachment(a.Base64, a.Name, "attachment"))],
            options.DisableLinkTracking ? new MailerSendSettings(TrackOpens: false, TrackClicks: false) : null);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mailersend.com/v1/email")
        {
            Content = JsonContent.Create(body, MailerSendJsonContext.Default.MailerSendRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.MailerSend.ApiKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (response.IsSuccessStatusCode)
        {
            var id = response.Headers.TryGetValues("x-message-id", out var ids) ? ids.FirstOrDefault() : null;
            var paused = response.Headers.TryGetValues("x-send-paused", out var flags) && flags.Any(f => string.Equals(f, "true", StringComparison.OrdinalIgnoreCase));
            if (paused)
            {
                return ProviderOutcome.Accepted(id is null ? [] : [id], "The account has paused sending, so the email was accepted but may not be sent until sending resumes.");
            }

            if (id is not null)
            {
                return ProviderOutcome.Accepted([id]);
            }

            // No id means nothing was sent. A readable body (the ALL_SUPPRESSED warning) is a clear rejection; an empty or
            // unreadable one fails closed (design 9.2).
            return json is null
                ? ProviderOutcome.Unreadable
                : ProviderOutcome.Rejected((int)response.StatusCode, null, "MailerSend accepted the request but sent nothing: every recipient may be suppressed.", retryable: false, quotaStop: false);
        }

        var code = json is null ? null : Code(json.RootElement);
        // MS42901 is the daily request quota; MS42903 is the per-minute rate limit, which clears by waiting.
        var quota = response.StatusCode == HttpStatusCode.TooManyRequests && code == "MS42901";
        return ProviderOutcome.ForStatus(response.StatusCode, code, Hint(response.StatusCode, code), quota);
    }

    private static string? Hint(HttpStatusCode status, string? code) => (status, code) switch
    {
        (HttpStatusCode.Unauthorized, _) => "Check EmailSender:MailerSend:ApiKey.",
        (HttpStatusCode.Forbidden, _) => "Check EmailSender:MailerSend:ApiKey, its scopes and its IP allow-list in MailerSend.",
        (_, "MS42207") => "Verify the sender domain in MailerSend.",
        (_, "MS42205") => "MailerSend allows one recipient per field until the account is approved.",
        _ => null,
    };

    // The code is the "#MSnnnnn" suffix of the top-level message (429) or of the first field error (422). Only the five digits
    // are taken, so the rest of the text, which can echo addresses, never leaves this method.
    private static string? Code(JsonElement root)
    {
        if (ResponseReader.String(root, "message") is { } message && CodePattern().Match(message) is { Success: true } direct)
        {
            return direct.Groups[1].Value;
        }

        if (ResponseReader.Walk(root, "errors") is { ValueKind: JsonValueKind.Object } errors)
        {
            foreach (var field in errors.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in field.Value.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && CodePattern().Match(entry.GetString()!) is { Success: true } nested)
                    {
                        return nested.Groups[1].Value;
                    }
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"#(MS\d{5})(?!\d)")]
    private static partial Regex CodePattern();
}
