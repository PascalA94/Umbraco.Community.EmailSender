using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class ElasticEmailProfile : IProviderProfile
{
    private const string Key = "EE-SENTINEL-KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.ElasticEmail;

    public string FixtureFolder => "elasticemail";

    public IReadOnlyList<string> Secrets => [Key];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.ElasticEmail, ElasticEmail = { ApiKey = Key } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:X-ElasticEmail-ApiKey"],
        RequiredPaths = [".Recipients", ".Recipients.To", ".Content", ".Content.From", ".Content.Subject", ".Content.Body"],
        ShapeExtras = [".Recipients.CC", ".Recipients.BCC", ".Content.Headers"],
        ReplyTo = ReplyToStyle.CommaSeparated,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        // The Idempotency-Key header is in the vendor's OpenAPI only, never seen live (design 9.13 row 17). Unknowns get the safe
        // default (design 9.11): no key, so no retry of a 5xx or a lost connection that may have been accepted.
        Idempotency = IdempotencyCarriage.None,
        // The schema has no content-id field.
        Inline = InlineContentId.None,
        MessageId = "-HHGPM_9RPhSMiaJq_ab4g3",
        // Error bodies are undocumented: status only.
        Auth = new(HttpStatusCode.Unauthorized, string.Empty, "Elastic Email rejected the email: HTTP 401. Check EmailSender:ElasticEmail:ApiKey."),
        Validation = new(HttpStatusCode.BadRequest, string.Empty, "Elastic Email rejected the email: HTTP 400."),
        MaxRequestBytes = 20 * 1024 * 1024,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F3"] = "Elastic Email documents no status or body for an unverified sender [unknown]: a 200 may precede a later suppression, so there is no signal to map.",
        ["F6"] = "Elastic Email documents no 429 and no quota signal: the vendor's skill file says v4 returns 5xx under load.",
        ["F8"] = "Elastic Email hides no failure in a 2xx body (a 200 is accepted only; outcomes arrive by status call or webhook).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var content = root.GetProperty("Content");
        var recipients = root.GetProperty("Recipients");
        var from = content.GetProperty("From").GetString()!;
        var open = from.LastIndexOf(" <", StringComparison.Ordinal);
        var parts = content.GetProperty("Body").EnumerateArray().ToList();
        return new RecordedEmail
        {
            FromAddress = open > 0 ? from[(open + 2)..^1] : from,
            FromName = open > 0 ? from[..open] : null,
            To = List(recipients, "To"),
            Cc = List(recipients, "CC"),
            Bcc = List(recipients, "BCC"),
            ReplyTo = content.TryGetProperty("ReplyTo", out var reply) ? reply.GetString()!.Split(',', StringSplitOptions.TrimEntries) : [],
            Subject = content.GetProperty("Subject").GetString(),
            Html = parts.FirstOrDefault(p => p.GetProperty("ContentType").GetString() == "HTML") is { ValueKind: JsonValueKind.Object } h ? h.GetProperty("Content").GetString() : null,
            Text = parts.FirstOrDefault(p => p.GetProperty("ContentType").GetString() == "PlainText") is { ValueKind: JsonValueKind.Object } t ? t.GetProperty("Content").GetString() : null,
            Headers = content.TryGetProperty("Headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = content.TryGetProperty("Attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("Name").GetString()!, a.GetProperty("ContentType").GetString()!, a.GetProperty("BinaryContent").GetString()!))]
                : [],
            TrackingOff = root.TryGetProperty("Options", out var options)
                && options.TryGetProperty("TrackOpens", out var opens) && options.TryGetProperty("TrackClicks", out var clicks)
                    ? opens.ValueKind == JsonValueKind.False && clicks.ValueKind == JsonValueKind.False
                    : null,
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
            Json = root,
        };
    }

    private static string[] List(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
}
