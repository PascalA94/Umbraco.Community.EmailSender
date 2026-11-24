using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class BirdProfile : IProviderProfile
{
    private const string Key = "bk_us1_SENTINEL_KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Bird;

    public string FixtureFolder => "bird";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Bird, Bird = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".from", ".from.email", ".to", ".subject"],
        // category and the tracking flags are sent on purpose (design 9.3): Bird defaults them to marketing and true.
        ShapeExtras = [".category", ".track_opens", ".track_clicks", ".cc", ".bcc", ".reply_to", ".headers"],
        ReplyTo = ReplyToStyle.List,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.Header,
        Inline = InlineContentId.ContentId,
        SuccessStatus = HttpStatusCode.Accepted,
        MessageId = "em_01krdgeqcxet5s7t44vh8rt9mg",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "Bird rejected the email: HTTP 401, E0xxxx. Check EmailSender:Bird:ApiKey."),
        Validation = new(HttpStatusCode.UnprocessableEntity, "response-error-validation.json", "Bird rejected the email: HTTP 422, E01001."),
        // Bird answers 422 for an unverified sender domain, with a delivery_error type (the validation error is validation_error).
        UnverifiedSender = new(HttpStatusCode.UnprocessableEntity, """{"error":{"type":"delivery_error","code":"E01020","name":"X","message":"SENTINEL-ECHO"}}""", "HTTP 422, E01020. Verify the sender domain in Bird"),
        MaxRequestBytes = 20 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "Bird documents no quota 429 apart from the rate limit (free-plan caps are described both as 429 and as rejected [unknown]); every 429 is a rate limit.",
        ["F8"] = "Bird hides no failure in a 2xx (design 9.2 body-parse table).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        // The object form is the one Bird's own basic example shows.
        var from = root.GetProperty("from");
        return new RecordedEmail
        {
            FromAddress = from.GetProperty("email").GetString(),
            FromName = from.TryGetProperty("name", out var name) ? name.GetString() : null,
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
            TrackingOff = root.TryGetProperty("track_opens", out var opens) && root.TryGetProperty("track_clicks", out var clicks)
                ? opens.ValueKind == JsonValueKind.False && clicks.ValueKind == JsonValueKind.False
                : null,
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
            Json = root,
        };
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static string[] List(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
}
