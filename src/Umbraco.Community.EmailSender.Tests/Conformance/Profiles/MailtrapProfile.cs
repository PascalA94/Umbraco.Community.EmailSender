using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MailtrapProfile : IProviderProfile
{
    private const string Key = "mt-SENTINEL-TOKEN"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Mailtrap;

    public string FixtureFolder => "mailtrap";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Mailtrap, Mailtrap = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".from", ".from.email", ".to", ".to[].email", ".subject"],
        ShapeExtras = [".cc", ".bcc", ".reply_to", ".headers"],
        ReplyTo = ReplyToStyle.Single,
        Headers = HeaderPolicy.SendAsGiven,
        // Tracking is not in the request schema [unknown]: a domain or stream setting, if anything.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        MessageId = "0c7fd939-02cf-11ed-88c2-0a58a9feac02",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.json", "Mailtrap rejected the email: HTTP 401. Mailtrap answers 401 for a bad token and for an unverified sending domain."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "Mailtrap rejected the email: HTTP 400."),
        // An unverified sending domain is answered with 401, the same as a bad token.
        UnverifiedSender = new(HttpStatusCode.Unauthorized, "response-error-auth.json", "for an unverified sending domain."),
        // Defensive: only /api/batch is documented to answer 200 on failure, and it is not used.
        FailureIn2xx = new(HttpStatusCode.OK, """{"success":false,"errors":["SENTINEL-ECHO"]}""", "Mailtrap rejected the email: HTTP 200."),
        MaxRequestBytes = 10 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "Mailtrap documents a 429 only for the sandbox API, and no quota signal: every 429 is a plain rate limit (an over-quota send may be refused with another code [unknown]).",
    };

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
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("filename").GetString()!, a.GetProperty("type").GetString()!, a.GetProperty("content").GetString()!,
                    a.TryGetProperty("disposition", out var d) && d.GetString() == "inline"))]
                : [],
            Json = root,
        };
    }

    private static string[] Emails(JsonElement root, string name)
        => root.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(a => a.GetProperty("email").GetString()!)] : [];
}
