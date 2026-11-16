using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>
/// Azure Communication Services through its SDK. The profile reads the JSON the SDK sends; there are no recorded examples,
/// so the label stays "Unverified" (design 9.7). The SDK owns the transport and retries are off (M2 ruling).
/// </summary>
internal sealed class AcsProfile : IProviderProfile
{
    private const string KeyRaw = "acs-SENTINEL-KEY"; // gitleaks:allow
    private const string Sdk = "SDK path, retries off (M2 ruling).";

    private static readonly string Key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(KeyRaw));

    public EmailProviderKind Kind => EmailProviderKind.AzureCommunicationServices;

    public string? FixtureFolder => null;

    public IReadOnlyList<string> Secrets => [KeyRaw, Key];

    public EmailSenderOptions Options() => new()
    {
        Provider = EmailProviderKind.AzureCommunicationServices,
        AzureCommunicationServices = { ConnectionString = $"endpoint=https://sentinel-resource.communication.azure.com/;accesskey={Key}" },
    };

    public ProfileExpectations Expect { get; } = new()
    {
        // The SDK signs each request with an HMAC of the key; the key itself is never sent.
        CredentialLocations = [],
        KeepsDisplayName = false,
        ReplyTo = ReplyToStyle.List,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        SuccessStatus = HttpStatusCode.Accepted,
        SuccessBody = """{"id":"operation-id-1","status":"Running"}""",
        SuccessHeaders = new Dictionary<string, string>
        {
            ["Operation-Location"] = "https://sentinel-resource.communication.azure.com/emails/operations/operation-id-1?api-version=2025-09-01",
        },
        MessageId = "operation-id-1",
        Auth = new(HttpStatusCode.Unauthorized, """{"error":{"code":"Denied","message":"SENTINEL-ECHO"}}""", "Azure Communication Services rejected the email: HTTP 401, error Denied. Check EmailSender:AzureCommunicationServices:ConnectionString."),
        Validation = new(HttpStatusCode.BadRequest, """{"error":{"code":"InvalidRequest","message":"SENTINEL-ECHO"}}""", "Azure Communication Services rejected the email: HTTP 400, error InvalidRequest."),
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["C11"] = Sdk,
        ["C12"] = "The SDK sets its own User-Agent; ACS keeps its own client registration (SDK transport, M2).",
        ["F3"] = "No recorded unverified-sender response for ACS (no fixtures; label Unverified).",
        ["F4"] = Sdk,
        ["F6"] = "ACS documents no quota 429.",
        ["F7"] = Sdk,
        ["F8"] = "ACS documents no failure inside a 202.",
        ["F9"] = "The SDK parses the 202 body itself; the package never reads it.",
        ["F11"] = "ACS request cap is undocumented [unknown]; the package cap applies (design 9.2 size table).",
        ["F14"] = "The SDK reads the response; the package never does.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var recipients = root.GetProperty("recipients");
        var content = root.GetProperty("content");
        return new RecordedEmail
        {
            FromAddress = root.GetProperty("senderAddress").GetString(),
            To = Addresses(recipients, "to"),
            Cc = Addresses(recipients, "cc"),
            Bcc = Addresses(recipients, "bcc"),
            ReplyTo = Addresses(root, "replyTo"),
            Subject = content.GetProperty("subject").GetString(),
            Html = content.TryGetProperty("html", out var html) ? html.GetString() : null,
            Text = content.TryGetProperty("plainText", out var text) ? text.GetString() : null,
            Headers = root.TryGetProperty("headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!)
                : new Dictionary<string, string>(),
            Attachments = root.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("name").GetString()!, a.GetProperty("contentType").GetString()!, a.GetProperty("contentInBase64").GetString()!,
                    a.TryGetProperty("contentId", out var id) && id.ValueKind == JsonValueKind.String))]
                : [],
            TrackingOff = root.TryGetProperty("userEngagementTrackingDisabled", out var tracking) && tracking.ValueKind == JsonValueKind.True ? true : null,
            Json = root,
        };
    }

    private static string[] Addresses(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(a => a.GetProperty("address").GetString()!)]
            : [];
}
