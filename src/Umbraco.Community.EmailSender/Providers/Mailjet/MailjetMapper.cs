using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Mailjet;

/// <summary>
/// Mailjet's <c>POST /v3.1/send</c> (design 9.2). A 2xx is not proof of acceptance: every <c>Messages[].Status</c> must be
/// <c>success</c>, and a 2xx body that cannot be read fails closed. Errors show status and the error code only; <c>ErrorMessage</c>
/// can echo addresses.
/// </summary>
internal sealed class MailjetMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.mailjet";

    private const string Endpoint = "https://api.mailjet.com/v3.1/send";

    public string Name => "Mailjet";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["api.mailjet.com"],
        // The whole payload, attachments included [docs]; at most 50 attachments (mj-0008).
        MaxRequestBytes = 15 * 1024 * 1024,
        MaxAttachments = 50,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.Single,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        FailClosedOn2xx = true,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var message = new MailjetMessage(
            new MailjetAddress(email.FromAddress, email.FromName),
            [.. email.To.Select(a => new MailjetAddress(a))],
            Addresses(email.Cc),
            Addresses(email.Bcc),
            email.ReplyTo.Count == 0 ? null : new MailjetAddress(email.ReplyTo[0]),
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            email.Headers.Count == 0 ? null : email.Headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new MailjetAttachment(a.ContentType, a.Name, a.Base64))],
            options.DisableLinkTracking ? "disabled" : null,
            options.DisableLinkTracking ? "disabled" : null);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new MailjetRequest([message]), MailjetJsonContext.Default.MailjetRequest),
        };
        // Built from parts and never logged: the public key is the username and the secret key the password.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.Mailjet.ApiKey}:{options.Mailjet.SecretKey}")));
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        var status = (int)response.StatusCode;
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Object)
        {
            return response.IsSuccessStatusCode ? ProviderOutcome.Unreadable : ProviderOutcome.ForStatus(response.StatusCode, null, Hint(status, null));
        }

        var root = json.RootElement;
        // Global errors (malformed JSON, bad credentials) have no Messages wrapper.
        if (!root.TryGetProperty("Messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
        {
            if (response.IsSuccessStatusCode)
            {
                return ProviderOutcome.Unreadable;
            }

            var globalCode = ResponseReader.String(root, "ErrorCode");
            return ProviderOutcome.ForStatus(response.StatusCode, globalCode, Hint(status, globalCode));
        }

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind == JsonValueKind.Object && ResponseReader.String(message, "Status") is "success")
            {
                continue;
            }

            // A failed message in a 2xx is not a send: the error's own status and code are shown, never its text.
            var error = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("Errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0 ? errors[0] : default;
            var code = error.ValueKind == JsonValueKind.Object ? ResponseReader.String(error, "ErrorCode") : null;
            var errorStatus = error.ValueKind == JsonValueKind.Object ? ResponseReader.Int(error, "StatusCode") : null;
            var shown = errorStatus is >= 400 and <= 599 ? errorStatus.Value : response.IsSuccessStatusCode ? 400 : status;
            return ProviderOutcome.Rejected(shown, code, Hint(shown, code), retryable: false, quotaStop: false);
        }

        // An all-success body on a non-2xx status is still not a send.
        if (!response.IsSuccessStatusCode)
        {
            return ProviderOutcome.ForStatus(response.StatusCode, null, Hint(status, null));
        }

        return MessageId(messages[0]) is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
    }

    private static string? MessageId(JsonElement message)
        => ResponseReader.Walk(message, "To") is { ValueKind: JsonValueKind.Array } to && to.GetArrayLength() > 0
            && ResponseReader.Walk(to[0], "MessageID") is { ValueKind: JsonValueKind.Number } id
            ? id.GetRawText()
            : null;

    private static string? Hint(int status, string? code) => (status, code) switch
    {
        (401, _) => "Check EmailSender:Mailjet:ApiKey and EmailSender:Mailjet:SecretKey.",
        (403, "send-0008" or "send-0007" or "send-0006") => "Verify the sender address or domain in Mailjet.",
        _ => null,
    };

    private static List<MailjetAddress>? Addresses(IReadOnlyList<string> values)
        => values.Count == 0 ? null : [.. values.Select(a => new MailjetAddress(a))];
}
