using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class ResendProfile : IProviderProfile
{
    private const string Key = "re_SENTINEL_KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Resend;

    public string FixtureFolder => "resend";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Resend, Resend = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".from", ".to", ".subject"],
        // The attachment fixture omits content_type; Resend derives it from the file name when absent, and we always send it.
        ShapeExtras = [".attachments[].content_type"],
        ReplyTo = ReplyToStyle.List,
        Headers = HeaderPolicy.SendAsGiven,
        // Tracking is a per-domain setting in Resend, off by default (design 9.2).
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.Header,
        Inline = InlineContentId.ContentId,
        MessageId = "49a3999c-0ce1-4ea6-ab68-afcd6dc2e794",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "Resend rejected the email: HTTP 401, missing_api_key. Check EmailSender:Resend:ApiKey."),
        Validation = new(HttpStatusCode.UnprocessableEntity, "response-error-validation.synthesised.json", "Resend rejected the email: HTTP 422, missing_required_field."),
        UnverifiedSender = new(HttpStatusCode.Forbidden, """{"statusCode":403,"name":"validation_error","message":"SENTINEL-ECHO"}""", "HTTP 403, validation_error. Verify the sender domain in Resend"),
        Quota = new(HttpStatusCode.TooManyRequests, """{"statusCode":429,"name":"daily_quota_exceeded","message":"SENTINEL-ECHO"}""", "Resend rejected the email: HTTP 429, daily_quota_exceeded."),
        MaxRequestBytes = 40 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F8"] = "Resend hides no failure in a 2xx (design 9.2 body-parse table).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var from = Str(root, "From") ?? Str(root, "from") ?? string.Empty;
        var open = from.LastIndexOf(" <", StringComparison.Ordinal);
        return new RecordedEmail
        {
            FromAddress = open > 0 ? from[(open + 2)..^1] : from,
            FromName = open > 0 ? from[..open] : null,
            To = List(root, "to"),
            Cc = List(root, "cc"),
            Bcc = List(root, "bcc"),
            ReplyTo = List(root, "reply_to"),
            Subject = Str(root, "subject"),
            Html = Str(root, "html"),
            Text = Str(root, "text"),
            Headers = root.TryGetProperty("headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("filename").GetString()!, a.GetProperty("content_type").GetString()!, a.GetProperty("content").GetString()!,
                    a.TryGetProperty("content_id", out _)))]
                : [],
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
            Json = root,
        };
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static string[] List(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
}
