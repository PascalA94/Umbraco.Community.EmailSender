using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MandrillProfile : IProviderProfile
{
    private const string Key = "md-SENTINEL-KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Mandrill;

    public string FixtureFolder => "mandrill";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Mandrill, Mandrill = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        // The key is a top-level body field: no header carries it.
        CredentialLocations = ["body"],
        RequiredPaths = [".key", ".message", ".message.from_email", ".message.to", ".message.to[].email", ".message.to[].type", ".message.subject"],
        ShapeExtras = [".message.from_name", ".message.headers", ".message.track_opens", ".message.track_clicks"],
        ReplyTo = ReplyToStyle.CustomHeader,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        FailClosedOn2xx = true,
        MessageId = "string",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "Mandrill rejected the email: HTTP 401, Invalid_Key. Check EmailSender:Mandrill:ApiKey."),
        // The validation-error shape is undocumented [unknown]: the documented envelope with a name is assumed.
        Validation = new(HttpStatusCode.BadRequest, """{"status":"error","code":-2,"name":"ValidationError","message":"SENTINEL-ECHO"}""", "Mandrill rejected the email: HTTP 400, ValidationError."),
        // An unverified sender is a rejected recipient with reject_reason unsigned inside a 200.
        UnverifiedSender = new(HttpStatusCode.OK, """[{"email":"SENTINEL@example.test","status":"rejected","reject_reason":"unsigned","_id":"a"}]""", "1 of 1 recipients rejected (reject_reason: unsigned). Verify the sender domain in Mandrill"),
        FailureIn2xx = new(HttpStatusCode.OK, """[{"email":"SENTINEL@example.test","status":"invalid","reject_reason":"invalid","_id":"a"}]""", "Mandrill rejected the email: HTTP 200. 1 of 1 recipients rejected (reject_reason: invalid)."),
        MaxRequestBytes = 10 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "Mandrill documents no 429 and no quota signal; an exhausted hourly quota queues the message (status queued).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var message = root.GetProperty("message");
        var recipients = message.GetProperty("to").EnumerateArray().Select(r => (Email: r.GetProperty("email").GetString()!, Type: r.GetProperty("type").GetString()!)).ToList();
        var headers = message.TryGetProperty("headers", out var h)
            ? h.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : new Dictionary<string, string>();
        return new RecordedEmail
        {
            FromAddress = message.GetProperty("from_email").GetString(),
            FromName = message.TryGetProperty("from_name", out var name) ? name.GetString() : null,
            To = [.. recipients.Where(r => r.Type == "to").Select(r => r.Email)],
            Cc = [.. recipients.Where(r => r.Type == "cc").Select(r => r.Email)],
            Bcc = [.. recipients.Where(r => r.Type == "bcc").Select(r => r.Email)],
            ReplyTo = headers.TryGetValue("Reply-To", out var reply) ? reply.Split(',', StringSplitOptions.TrimEntries) : [],
            Subject = message.GetProperty("subject").GetString(),
            Html = message.TryGetProperty("html", out var html) ? html.GetString() : null,
            Text = message.TryGetProperty("text", out var text) ? text.GetString() : null,
            Headers = headers.Where(p => p.Key != "Reply-To").ToDictionary(p => p.Key, p => p.Value),
            Attachments = message.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("name").GetString()!, a.GetProperty("type").GetString()!, a.GetProperty("content").GetString()!))]
                : [],
            TrackingOff = message.TryGetProperty("track_opens", out var opens) && message.TryGetProperty("track_clicks", out var clicks)
                ? opens.ValueKind == JsonValueKind.False && clicks.ValueKind == JsonValueKind.False
                : null,
            Json = root,
        };
    }
}
