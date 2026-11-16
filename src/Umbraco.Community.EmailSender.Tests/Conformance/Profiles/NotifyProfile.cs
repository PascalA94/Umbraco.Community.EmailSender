using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>GOV.UK Notify: per-recipient policy, no recorded examples (built before the fixtures; label "Unverified").</summary>
internal sealed class NotifyProfile : IProviderProfile
{
    private const string ServiceId = "11111111-2222-3333-4444-555555555555";
    private const string Secret = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ApiKey = "sentinel-key-" + ServiceId + "-" + Secret; // gitleaks:allow
    private const string NotificationId = "740e5834-3a29-46b4-9a6f-16142fde533a";

    public EmailProviderKind Kind => EmailProviderKind.GovUkNotify;

    public string? FixtureFolder => null;

    public IReadOnlyList<string> Secrets => [ApiKey, Secret];

    public EmailSenderOptions Options() => new()
    {
        Provider = EmailProviderKind.GovUkNotify,
        GovUkNotify = { ApiKey = ApiKey, EmailTemplateId = "99999999-8888-7777-6666-555555555555" },
    };

    public ProfileExpectations Expect { get; } = new()
    {
        // The key signs a JWT; neither the key nor its secret half is ever sent.
        CredentialLocations = [],
        SupportsAttachments = false,
        HtmlAsText = true,
        ReplyTo = ReplyToStyle.NotSupported,
        Headers = HeaderPolicy.DropAll,
        Tracking = TrackingStyle.NotApplicable,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.None,
        SuccessStatus = HttpStatusCode.Created,
        SuccessBody = $$"""{"id":"{{NotificationId}}"}""",
        MessageId = NotificationId,
        Auth = new(HttpStatusCode.Forbidden, """{"errors":[{"error":"AuthError","message":"SENTINEL-ECHO"}],"status_code":403}""", "GOV.UK Notify rejected the email: HTTP 403, AuthError. Check EmailSender:GovUkNotify:ApiKey."),
        Validation = new(HttpStatusCode.BadRequest, """{"errors":[{"error":"ValidationError","message":"SENTINEL-ECHO"}],"status_code":400}""", "GOV.UK Notify rejected the email: HTTP 400, ValidationError."),
        UnverifiedSender = new(HttpStatusCode.BadRequest, """{"errors":[{"error":"BadRequestError","message":"Can't send to this recipient when service is in trial mode"}],"status_code":400}""", "team-only or the service is in trial mode"),
        Quota = new((HttpStatusCode)429, """{"errors":[{"error":"TooManyRequestsError","message":"SENTINEL-ECHO"}],"status_code":429}""", "GOV.UK Notify rejected the email: HTTP 429, TooManyRequestsError."),
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["C3"] = "No From: the Notify service fixes the sender (UsesFromAddress is false).",
        ["C4"] = "Cc and Bcc fail before sending (NotifyProviderTests.CcAndBcc_FailClearlyBeforeSending); Reply-To is covered by MessageReplyTo_IsIgnoredWithAWarningThatNamesNoAddress.",
        ["C6"] = "Attachments fail before sending (NotifyProviderTests.Attachments_FailClearlyBeforeSending).",
        ["C7"] = "Attachments fail before sending (NotifyProviderTests.Attachments_FailClearlyBeforeSending).",
        ["F8"] = "Notify documents no failure inside a 201.",
        ["F11"] = "No documented request cap; Notify takes no attachments and the package cap applies.",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var root = JsonDocument.Parse(body).RootElement.Clone();
        var personalisation = root.GetProperty("personalisation");
        return new RecordedEmail
        {
            To = [root.GetProperty("email_address").GetString()!],
            Subject = personalisation.GetProperty("subject").GetString(),
            Text = personalisation.GetProperty("body").GetString(),
            Json = root,
        };
    }
}
