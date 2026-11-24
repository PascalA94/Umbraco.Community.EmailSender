using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class BrevoProfile : IProviderProfile
{
    private const string Key = "xkeysib-SENTINEL-KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Brevo;

    public string FixtureFolder => "brevo";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Brevo, Brevo = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:api-key"],
        RequiredPaths = [".sender", ".to", ".subject"],
        // Cc, Bcc, Reply-To and the idempotency key are not in the fixtures; the OpenAPI schema has them.
        ShapeExtras = [".cc", ".bcc", ".replyTo", ".headers", ".textContent"],
        ReplyTo = ReplyToStyle.Single,
        // Brevo does not support standard email headers [docs]: all are dropped, Expires included. The idempotency key rides in
        // the body's headers object, which is not a custom header.
        Headers = HeaderPolicy.DropAll,
        // Account-level; pixel removal is Enterprise only (design 9.2).
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.Body,
        Inline = InlineContentId.None,
        SuccessStatus = HttpStatusCode.Created,
        MessageId = "<201798300811.5787683@relay.domain.com>",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "Brevo rejected the email: HTTP 401, unauthorized. Check EmailSender:Brevo:ApiKey."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "Brevo rejected the email: HTTP 400, invalid_parameter."),
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F3"] = "Brevo's response to an unvalidated sender is undocumented [unknown, carried, docs/spikes.md].",
        ["F6"] = "Brevo documents no quota 429; every 429 is a plain rate limit.",
        ["F8"] = "Brevo hides no failure in a 2xx (design 9.2 body-parse table).",
        ["F11"] = "Brevo documents no request size cap [unknown]: only the package cap applies.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var sender = root.GetProperty("sender");
        var headers = root.TryGetProperty("headers", out var h) ? h : (JsonElement?)null;
        return new RecordedEmail
        {
            FromAddress = sender.GetProperty("email").GetString(),
            FromName = sender.TryGetProperty("name", out var name) ? name.GetString() : null,
            To = Emails(root, "to"),
            Cc = Emails(root, "cc"),
            Bcc = Emails(root, "bcc"),
            ReplyTo = root.TryGetProperty("replyTo", out var reply) ? [reply.GetProperty("email").GetString()!] : [],
            Subject = root.GetProperty("subject").GetString(),
            Html = root.TryGetProperty("htmlContent", out var html) ? html.GetString() : null,
            Text = root.TryGetProperty("textContent", out var text) ? text.GetString() : null,
            Headers = headers?.EnumerateObject().Where(p => p.Name != "idempotencyKey").ToDictionary(p => p.Name, p => p.Value.GetString()!)
                ?? new Dictionary<string, string>(),
            // Brevo takes no content type: it infers one from the file name, so the profile does the same.
            Attachments = root.TryGetProperty("attachment", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("name").GetString()!, TypeOf(a.GetProperty("name").GetString()!), a.GetProperty("content").GetString()!))]
                : [],
            IdempotencyKey = headers is { } given && given.TryGetProperty("idempotencyKey", out var key) ? key.GetString() : null,
            Json = root,
        };
    }

    private static string TypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    private static string[] Emails(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(a => a.GetProperty("email").GetString()!)] : [];
}
