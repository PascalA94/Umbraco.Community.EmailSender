using System.Net;
using System.Net.Http.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Brevo;

/// <summary>
/// Brevo's <c>POST /v3/smtp/email</c> (design 9.2). The idempotency key travels in the body's <c>headers</c> object, the only
/// custom header Brevo takes. A repeated key within 30 minutes returns <c>duplicate_parameter</c> and sends nothing.
/// </summary>
internal sealed class BrevoMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.brevo";

    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    public string Name => "Brevo";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["api.brevo.com"],
        // No documented size cap [unknown]: only the package cap applies.
        // "Standard email headers are not supported" [docs]: the pipeline drops every custom header, Expires included.
        Headers = HeaderPolicy.DropAll,
        ReplyTo = ReplyToStyle.Single,
        // Account-level; removing the pixel is an Enterprise feature, so the README says where to switch it off.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.Body,
        // Attachments carry a name and content only, so there is no content id.
        Inline = InlineContentId.None,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var body = new BrevoRequest(
            new BrevoAddress(email.FromAddress, email.FromName),
            [.. email.To.Select(a => new BrevoAddress(a))],
            Addresses(email.Cc),
            Addresses(email.Bcc),
            email.ReplyTo.Count == 0 ? null : new BrevoAddress(email.ReplyTo[0]),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            new Dictionary<string, string> { ["idempotencyKey"] = attempt.IdempotencyKey },
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new BrevoAttachment(a.Name, a.Base64))]);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body, BrevoJsonContext.Default.BrevoRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Add("api-key", options.Brevo.ApiKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body) => Read(response, body, retry: false);

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body, SendAttempt attempt) => Read(response, body, attempt.Number > 1);

    private static ProviderOutcome Read(HttpResponseMessage response, ReadOnlyMemory<byte> body, bool retry)
    {
        using var json = ResponseReader.TryParse(body);
        if (response.IsSuccessStatusCode)
        {
            if (json is null)
            {
                return ProviderOutcome.Unreadable;
            }

            return ResponseReader.String(json.RootElement, "messageId") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
        }

        var code = json is null ? null : ResponseReader.String(json.RootElement, "code");
        // Our own key was refused as a duplicate on a retry: the earlier attempt reached Brevo and was accepted [docs: a duplicate
        // returns an error, not the original response], so the id is unknown. The status is undocumented, so any 4xx counts. On a first attempt it is an ordinary failure.
        if (retry && (int)response.StatusCode is >= 400 and < 500 && code == "duplicate_parameter")
        {
            return ProviderOutcome.Accepted([], "Brevo reported a duplicate request on a retry, so the first attempt was accepted; no message id is available.");
        }

        return ProviderOutcome.ForStatus(response.StatusCode, code, Hint(response.StatusCode));
    }

    private static string? Hint(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Check EmailSender:Brevo:ApiKey.",
        HttpStatusCode.PaymentRequired => "The Brevo account needs activation or more credits.",
        HttpStatusCode.Forbidden => "Check that the sender is validated in Brevo, and that the API key's permissions and IP allow-list include this server.",
        _ => null,
    };

    private static List<BrevoAddress>? Addresses(IReadOnlyList<string> values)
        => values.Count == 0 ? null : [.. values.Select(a => new BrevoAddress(a))];
}
