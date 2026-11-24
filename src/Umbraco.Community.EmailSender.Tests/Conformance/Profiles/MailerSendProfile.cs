using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MailerSendProfile : IProviderProfile
{
    private const string Key = "mlsn.SENTINEL_KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.MailerSend;

    public string FixtureFolder => "mailersend";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.MailerSend, MailerSend = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".from", ".from.email", ".to", ".to[].email", ".subject"],
        ShapeExtras = [".cc", ".bcc", ".reply_to", ".settings", ".attachments[].disposition"],
        ReplyTo = ReplyToStyle.Single,
        // Custom headers are a Professional-plan feature (422 MS42233 otherwise), so none is sent (design 9.3).
        Headers = HeaderPolicy.DropAll,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        FailClosedOn2xx = true,
        SuccessStatus = HttpStatusCode.Accepted,
        // The documented success body is empty; the id is the x-message-id header only.
        SuccessBody = string.Empty,
        SuccessHeaders = new Dictionary<string, string> { ["x-message-id"] = "5e42957d51f1d94a1070a733" },
        MessageId = "5e42957d51f1d94a1070a733",
        MaxCc = 10,
        MaxBcc = 10,
        // The 401 body is synthesised [unknown]: status only.
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "MailerSend rejected the email: HTTP 401. Check EmailSender:MailerSend:ApiKey."),
        Validation = new(HttpStatusCode.UnprocessableEntity, """{"message":"The given data was invalid.","errors":{"subject":["The subject field is required. #MS42209 SENTINEL-ECHO"]}}""", "MailerSend rejected the email: HTTP 422, MS42209."),
        UnverifiedSender = new(HttpStatusCode.UnprocessableEntity, "response-error-validation.json", "HTTP 422, MS42207. Verify the sender domain in MailerSend"),
        Quota = new(HttpStatusCode.TooManyRequests, """{"message":"Your account reached its daily request quota. #MS42901 SENTINEL-ECHO"}""", "MailerSend rejected the email: HTTP 429, MS42901."),
        // A 202 with no x-message-id sent nothing (every recipient suppressed).
        FailureIn2xx = new(HttpStatusCode.Accepted, """{"warnings":[{"type":"ALL_SUPPRESSED","message":"SENTINEL-ECHO"}]}""", "MailerSend rejected the email: HTTP 202."),
        MaxRequestBytes = 25 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>();

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var from = root.GetProperty("from");
        return new RecordedEmail
        {
            FromAddress = from.GetProperty("email").GetString(),
            FromName = from.TryGetProperty("name", out var name) ? name.GetString() : null,
            To = Emails(root, "to"),
            Cc = Emails(root, "cc"),
            Bcc = Emails(root, "bcc"),
            ReplyTo = root.TryGetProperty("reply_to", out var reply) ? [reply.GetProperty("email").GetString()!] : [],
            Subject = root.GetProperty("subject").GetString(),
            Html = root.TryGetProperty("html", out var html) ? html.GetString() : null,
            Text = root.TryGetProperty("text", out var text) ? text.GetString() : null,
            Headers = root.TryGetProperty("headers", out var headers)
                ? headers.EnumerateArray().ToDictionary(h => h.GetProperty("name").GetString()!, h => h.GetProperty("value").GetString()!)
                : new Dictionary<string, string>(),
            // MailerSend has no content-type field: it derives the type from the file name, so the profile does the same.
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("filename").GetString()!,
                    MimeKit.MimeTypes.GetMimeType(a.GetProperty("filename").GetString()!),
                    a.GetProperty("content").GetString()!,
                    a.TryGetProperty("disposition", out var d) && d.GetString() == "inline"))]
                : [],
            TrackingOff = root.TryGetProperty("settings", out var settings)
                && settings.TryGetProperty("track_opens", out var opens) && settings.TryGetProperty("track_clicks", out var clicks)
                    ? opens.ValueKind == JsonValueKind.False && clicks.ValueKind == JsonValueKind.False
                    : null,
            Json = root,
        };
    }

    private static string[] Emails(JsonElement root, string name)
        => root.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(a => a.GetProperty("email").GetString()!)] : [];
}
