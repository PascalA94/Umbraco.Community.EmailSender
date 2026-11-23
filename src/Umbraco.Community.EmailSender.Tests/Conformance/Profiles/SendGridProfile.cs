using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class SendGridProfile : IProviderProfile
{
    private const string Key = "SG.SENTINEL-KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.SendGrid;

    public string FixtureFolder => "sendgrid";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.SendGrid, SendGrid = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".personalizations", ".from", ".subject", ".content"],
        // The fixtures show reply_to (one object); we send reply_to_list (an array) so several Reply-Tos survive.
        ShapeExtras = [".reply_to_list"],
        ReplyTo = ReplyToStyle.List,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        // 202 with an empty body: the id header is undocumented, so "none" is the normal case.
        SuccessStatus = HttpStatusCode.Accepted,
        SuccessBody = string.Empty,
        MessageId = "none",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.json", "SendGrid rejected the email: HTTP 401. Check EmailSender:SendGrid:ApiKey."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "SendGrid rejected the email: HTTP 400."),
        UnverifiedSender = new(HttpStatusCode.Forbidden, """{"errors":[{"message":"SENTINEL-ECHO"}]}""", "HTTP 403. Verify the sender or domain in SendGrid"),
        MaxRequestBytes = 20 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "SendGrid documents no quota 429; every 429 is a plain rate limit.",
        ["F8"] = "SendGrid hides no failure in a 2xx (design 9.2 body-parse table).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var person = root.GetProperty("personalizations")[0];
        var content = root.GetProperty("content").EnumerateArray().ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("value").GetString());
        var from = root.GetProperty("from");
        return new RecordedEmail
        {
            FromAddress = from.GetProperty("email").GetString(),
            FromName = from.TryGetProperty("name", out var name) ? name.GetString() : null,
            To = Emails(person, "to"),
            Cc = Emails(person, "cc"),
            Bcc = Emails(person, "bcc"),
            ReplyTo = Emails(root, "reply_to_list"),
            Subject = root.GetProperty("subject").GetString(),
            Html = content.GetValueOrDefault("text/html"),
            Text = content.GetValueOrDefault("text/plain"),
            Headers = root.TryGetProperty("headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("filename").GetString()!, a.GetProperty("type").GetString()!, a.GetProperty("content").GetString()!,
                    a.TryGetProperty("disposition", out var d) && d.GetString() == "inline"))]
                : [],
            TrackingOff = root.TryGetProperty("tracking_settings", out var tracking)
                ? !tracking.GetProperty("click_tracking").GetProperty("enable").GetBoolean() && !tracking.GetProperty("open_tracking").GetProperty("enable").GetBoolean()
                : null,
            Json = root,
        };
    }

    private static string[] Emails(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(a => a.GetProperty("email").GetString()!)] : [];
}
