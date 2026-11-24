using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MicrosoftGraphProfile : IProviderProfile
{
    internal const string ClientSecret = "gs-SENTINEL-SECRET"; // gitleaks:allow
    internal const string Token = "gt-SENTINEL-TOKEN"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.MicrosoftGraph;

    public string FixtureFolder => "graph";

    // The client secret never reaches the send request; the token only ever travels in the Authorization header.
    public IReadOnlyList<string> Secrets => [ClientSecret, Token];

    public EmailSenderOptions Options() => new()
    {
        Provider = EmailProviderKind.MicrosoftGraph,
        MicrosoftGraph =
        {
            TenantId = "33333333-4444-5555-6666-777777777777",
            ClientId = "535fb089-9ff3-47b6-9bfb-4f1264799865",
            ClientSecret = ClientSecret,
            SenderUserId = "22222222-3333-4444-5555-666666666666",
        },
    };

    public Func<HttpRequestMessage, HttpResponseMessage?>? Auxiliary { get; } = request
        => request.RequestUri?.Host == "login.microsoftonline.com"
            ? FakeProviderServer.Answer(HttpStatusCode.OK, $$"""{"token_type":"Bearer","expires_in":3599,"ext_expires_in":3599,"access_token":"{{Token}}"}""")
            : null;

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = [".message", ".message.subject", ".message.body.contentType", ".message.body.content", ".message.toRecipients[].emailAddress.address"],
        // The docs example writes saveToSentItems as the string "false"; the API takes a boolean. Bcc and replyTo have no recorded example.
        ShapeExtras = [".saveToSentItems", ".message.bccRecipients", ".message.replyTo"],
        // Graph sends as the mailbox, so a From is never in the request.
        KeepsDisplayName = false,
        ReplyTo = ReplyToStyle.List,
        Headers = HeaderPolicy.XPrefixedOnly,
        Tracking = TrackingStyle.NotApplicable,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        SuccessStatus = HttpStatusCode.Accepted,
        SuccessBody = string.Empty,
        MessageId = "none",
        Auth = new(
            HttpStatusCode.Forbidden,
            """{"error":{"code":"ErrorAccessDenied","message":"SENTINEL-ECHO Access is denied."}}""",
            "Microsoft Graph rejected the email: HTTP 403, ErrorAccessDenied. Check the app's Mail.Send permission, admin consent and that the mailbox is inside the RBAC scope."),
        Validation = new(HttpStatusCode.BadRequest, "response-error-validation.json", "Microsoft Graph rejected the email: HTTP 400, badRequest."),
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["C3"] = "Graph sends as the mailbox in SenderUserId, so no From is in the request (design 9.4); GraphMapperTests.MessageFrom_IgnoredWithDebug covers it.",
        ["C11"] = "Graph has no idempotency key and a 503 may follow an accepted send, so only a 429 is retried (design 9.4, security review M9); GraphMapperTests.ServerErrors_IncludingA503_AreNeverRetried covers it.",
        ["F3"] = "The error codes for an unlicensed or out-of-scope mailbox are undocumented [unknown, design 9.11], so every refusal is status only.",
        ["F6"] = "Graph documents no quota signal: every 429 is a plain rate limit.",
        ["F8"] = "A 2xx is accepted whatever its body: none of the documented 2xx answers hides a failure [docs].",
        ["F11"] = "The scenario sends one large attachment, which the 3 MB per-attachment rule refuses first (GraphMapperTests.AttachmentOver3Mb_RefusedBeforeSend); the 35 MB request cap is covered by GraphMapperTests.RequestOver35Mb_RefusedBeforeSend.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var message = root.GetProperty("message");
        var content = message.GetProperty("body");
        var isHtml = content.GetProperty("contentType").GetString() == "HTML";
        return new RecordedEmail
        {
            To = Addresses(message, "toRecipients"),
            Cc = Addresses(message, "ccRecipients"),
            Bcc = Addresses(message, "bccRecipients"),
            ReplyTo = Addresses(message, "replyTo"),
            Subject = message.GetProperty("subject").GetString(),
            Html = isHtml ? content.GetProperty("content").GetString() : null,
            Text = isHtml ? null : content.GetProperty("content").GetString(),
            Headers = message.TryGetProperty("internetMessageHeaders", out var headers)
                ? headers.EnumerateArray().ToDictionary(h => h.GetProperty("name").GetString()!, h => h.GetProperty("value").GetString()!)
                : new Dictionary<string, string>(),
            Attachments = message.TryGetProperty("attachments", out var attachments)
                ? [.. attachments.EnumerateArray().Select(a => new RecordedAttachment(
                    a.GetProperty("name").GetString()!, a.GetProperty("contentType").GetString()!, a.GetProperty("contentBytes").GetString()!,
                    a.TryGetProperty("isInline", out var inline) && inline.GetBoolean()))]
                : [],
            Json = root,
        };
    }

    private static string[] Addresses(JsonElement message, string name)
        => message.TryGetProperty(name, out var list)
            ? [.. list.EnumerateArray().Select(a => a.GetProperty("emailAddress").GetProperty("address").GetString()!)]
            : [];
}
