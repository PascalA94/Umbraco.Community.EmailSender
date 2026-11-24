using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class Smtp2GoProfile : IProviderProfile
{
    private const string Key = "api-SENTINEL_KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Smtp2Go;

    public string FixtureFolder => "smtp2go";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Smtp2Go, Smtp2Go = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:X-Smtp2go-Api-Key"],
        RequiredPaths = [".sender", ".to", ".subject"],
        ShapeExtras = [".cc", ".bcc", ".custom_headers"],
        // No Reply-To field: the address goes in a custom header (design 9.3).
        ReplyTo = ReplyToStyle.CustomHeader,
        Headers = HeaderPolicy.SendAsGiven,
        // Tracking is a setting on the API key; the README says where.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.FileNameOnly,
        FailClosedOn2xx = true,
        MessageId = "1u0SwL-B9zBpi9ffUq-JAB2",
        // The 401 body is undocumented [unknown]: status only.
        Auth = new(HttpStatusCode.Unauthorized, string.Empty, "SMTP2GO rejected the email: HTTP 401. Check EmailSender:Smtp2Go:ApiKey."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "SMTP2GO rejected the email: HTTP 400, E_ApiResponseCodes.NON_VALIDATING_IN_PAYLOAD."),
        // The API's answer for an unverified sender is undocumented [unknown]; a 200 with failures is the documented shape.
        UnverifiedSender = new(HttpStatusCode.OK, "response-200-with-failure.synthesised.json", "SMTP2GO rejected the email: HTTP 200. Verify the sender domain or address in SMTP2GO."),
        FailureIn2xx = new(HttpStatusCode.OK, """{"request_id":"x","data":{"succeeded":0,"failed":1,"failures":["SENTINEL-ECHO"],"email_id":""}}""", "SMTP2GO rejected the email: HTTP 200."),
        MaxRequestBytes = 50 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "SMTP2GO documents no quota 429; every 429 is the abuse or per-key rate limit.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var sender = root.GetProperty("sender").GetString()!;
        var open = sender.LastIndexOf(" <", StringComparison.Ordinal);
        var customHeaders = root.TryGetProperty("custom_headers", out var list)
            ? list.EnumerateArray().Select(h => (Name: h.GetProperty("header").GetString()!, Value: h.GetProperty("value").GetString()!)).ToList()
            : [];
        return new RecordedEmail
        {
            FromAddress = open > 0 ? sender[(open + 2)..^1] : sender,
            FromName = open > 0 ? sender[..open] : null,
            To = List(root, "to"),
            Cc = List(root, "cc"),
            Bcc = List(root, "bcc"),
            ReplyTo = customHeaders.Where(h => h.Name == "Reply-To").SelectMany(h => h.Value.Split(',', StringSplitOptions.TrimEntries)).ToArray(),
            Subject = root.GetProperty("subject").GetString(),
            Html = root.TryGetProperty("html_body", out var html) ? html.GetString() : null,
            Text = root.TryGetProperty("text_body", out var text) ? text.GetString() : null,
            Headers = customHeaders.Where(h => h.Name != "Reply-To").ToDictionary(h => h.Name, h => h.Value),
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("filename").GetString()!, a.GetProperty("mimetype").GetString()!, a.GetProperty("fileblob").GetString()!))]
                : [],
            Json = root,
        };
    }

    private static string[] List(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
}
