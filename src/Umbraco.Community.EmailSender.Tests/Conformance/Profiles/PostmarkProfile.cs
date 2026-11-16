using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class PostmarkProfile : IProviderProfile
{
    private const string Token = "pm-SENTINEL-TOKEN"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Postmark;

    public string FixtureFolder => "postmark";

    public IReadOnlyList<string> Secrets => [Token];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Postmark, Postmark = { ServerToken = Token } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:X-Postmark-Server-Token"],
        RequiredPaths = [".From", ".To", ".Subject", ".MessageStream"],
        ReplyTo = ReplyToStyle.CommaSeparated,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        MessageId = "0a129aee-e1cd-480d-b08d-4f48548ff48d",
        Auth = new(HttpStatusCode.Unauthorized, "response-error-auth.synthesised.json", "Postmark rejected the email: HTTP 401, ErrorCode 10. Check EmailSender:Postmark:ServerToken."),
        Validation = new(HttpStatusCode.UnprocessableEntity, "response-error-validation.json", "Postmark rejected the email: HTTP 422, ErrorCode 403."),
        // The unconfirmed-signature ErrorCode is undocumented (docs/spikes.md, M6); pending approval (412) is the documented
        // sender-side refusal with a fixed hint.
        UnverifiedSender = new(HttpStatusCode.UnprocessableEntity, """{"ErrorCode":412,"Message":"SENTINEL-ECHO"}""", "HTTP 422, ErrorCode 412. While the Postmark account is pending approval"),
        // Defensive: the single-send endpoint documents non-200 on failure, but a 200 with a non-zero ErrorCode is not a send.
        FailureIn2xx = new(HttpStatusCode.OK, """{"ErrorCode":406,"Message":"SENTINEL-ECHO","MessageID":"x"}""", "Postmark rejected the email: HTTP 200, ErrorCode 406."),
        MaxRequestBytes = 10 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F6"] = "Postmark documents no quota 429; every 429 is a plain rate limit.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var from = Str(root, "From") ?? string.Empty;
        var open = from.LastIndexOf(" <", StringComparison.Ordinal);
        return new RecordedEmail
        {
            FromAddress = open > 0 ? from[(open + 2)..^1] : from,
            FromName = open > 0 ? from[..open] : null,
            To = List(root, "To"),
            Cc = List(root, "Cc"),
            Bcc = List(root, "Bcc"),
            ReplyTo = List(root, "ReplyTo"),
            Subject = Str(root, "Subject"),
            Html = Str(root, "HtmlBody"),
            Text = Str(root, "TextBody"),
            Headers = root.TryGetProperty("Headers", out var headers)
                ? headers.EnumerateArray().ToDictionary(h => h.GetProperty("Name").GetString()!, h => h.GetProperty("Value").GetString()!)
                : new Dictionary<string, string>(),
            Attachments = root.TryGetProperty("Attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("Name").GetString()!, a.GetProperty("ContentType").GetString()!, a.GetProperty("Content").GetString()!,
                    a.TryGetProperty("ContentID", out _)))]
                : [],
            TrackingOff = root.TryGetProperty("TrackLinks", out var links)
                ? links.GetString() == "None" && root.TryGetProperty("TrackOpens", out var opens) && opens.ValueKind == JsonValueKind.False
                : null,
            Json = root,
        };
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static string[] List(JsonElement root, string name)
        => Str(root, name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
