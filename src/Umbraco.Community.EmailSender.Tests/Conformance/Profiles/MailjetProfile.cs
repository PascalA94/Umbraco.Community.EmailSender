using System.Net;
using System.Text;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MailjetProfile : IProviderProfile
{
    private const string PublicKey = "mj-PUBLIC-SENTINEL"; // gitleaks:allow
    private const string SecretKey = "mj-SECRET-SENTINEL"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Mailjet;

    public string FixtureFolder => "mailjet";

    // Both keys are secrets, and so is the Basic credential they make: logging the Authorization header would leak both.
    public IReadOnlyList<string> Secrets => [PublicKey, SecretKey, Convert.ToBase64String(Encoding.ASCII.GetBytes($"{PublicKey}:{SecretKey}"))];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Mailjet, Mailjet = { ApiKey = PublicKey, SecretKey = SecretKey } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".Messages", ".Messages[].From", ".Messages[].To", ".Messages[].Subject"],
        ShapeExtras = [".Messages[].Cc", ".Messages[].Bcc", ".Messages[].ReplyTo", ".Messages[].Headers", ".Messages[].TrackOpens", ".Messages[].TrackClicks"],
        ReplyTo = ReplyToStyle.Single,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        // A failure can hide in a 2xx, so an unreadable 2xx body fails closed (design 9.2).
        FailClosedOn2xx = true,
        SuccessStatus = HttpStatusCode.Created,
        MessageId = "20547681647433000",
        // The 401 body is undocumented [unknown]: status only.
        Auth = new(HttpStatusCode.Unauthorized, string.Empty, "Mailjet rejected the email: HTTP 401. Check EmailSender:Mailjet:ApiKey and EmailSender:Mailjet:SecretKey."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "Mailjet rejected the email: HTTP 400, mj-0004."),
        UnverifiedSender = new(HttpStatusCode.Forbidden, """{"Messages":[{"Status":"error","Errors":[{"ErrorCode":"send-0008","StatusCode":403,"ErrorMessage":"SENTINEL-ECHO"}]}]}""", "HTTP 403, send-0008. Verify the sender address or domain in Mailjet"),
        // The HTTP status of a failed message inside a 2xx is undocumented [unknown]: the error's own StatusCode is shown.
        FailureIn2xx = new(HttpStatusCode.OK, """{"Messages":[{"Status":"error","Errors":[{"ErrorCode":"mj-0004","StatusCode":400,"ErrorMessage":"SENTINEL-ECHO"}]}]}""", "Mailjet rejected the email: HTTP 400, mj-0004."),
        MaxRequestBytes = 15 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "Mailjet documents no quota 429; every 429 is a plain rate limit.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var message = root.GetProperty("Messages")[0];
        var from = message.GetProperty("From");
        return new RecordedEmail
        {
            FromAddress = from.GetProperty("Email").GetString(),
            FromName = from.TryGetProperty("Name", out var name) ? name.GetString() : null,
            To = Emails(message, "To"),
            Cc = Emails(message, "Cc"),
            Bcc = Emails(message, "Bcc"),
            ReplyTo = message.TryGetProperty("ReplyTo", out var reply) ? [reply.GetProperty("Email").GetString()!] : [],
            Subject = message.GetProperty("Subject").GetString(),
            Html = message.TryGetProperty("HTMLPart", out var html) ? html.GetString() : null,
            Text = message.TryGetProperty("TextPart", out var text) ? text.GetString() : null,
            Headers = message.TryGetProperty("Headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = message.TryGetProperty("Attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("Filename").GetString()!, a.GetProperty("ContentType").GetString()!, a.GetProperty("Base64Content").GetString()!))]
                : [],
            TrackingOff = message.TryGetProperty("TrackOpens", out var opens) && message.TryGetProperty("TrackClicks", out var clicks)
                ? opens.GetString() == "disabled" && clicks.GetString() == "disabled"
                : null,
            Json = root,
        };
    }

    private static string[] Emails(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(a => a.GetProperty("Email").GetString()!)] : [];
}
