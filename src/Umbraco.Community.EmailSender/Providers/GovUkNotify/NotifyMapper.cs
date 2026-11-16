using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

/// <summary>
/// GOV.UK Notify's <c>POST /v2/notifications/email</c> with one template whose subject is <c>((subject))</c> and whose body
/// is <c>((body))</c>. Notify takes one recipient per request and has no attachments, Cc, Bcc or free-form reply-to: the
/// pipeline refuses the first three before sending and ignores a reply-to with a warning. Failures carry the HTTP status and
/// Notify's error class name only, never the response text, which can echo the recipient.
/// </summary>
internal sealed partial class NotifyMapper(TimeProvider clock) : IProviderMapper
{
    public const string ClientName = "community.emailSender.govUkNotify";

    private const string Host = "https://api.notifications.service.gov.uk/";

    public string Name => "GOV.UK Notify";

    string IProviderMapper.ClientName => ClientName;

    // The sender address is fixed in the Notify service, so a From is never required or sent.
    public bool UsesFromAddress => false;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        MaxAttachments = 0,
        MaxCc = 0,
        MaxBcc = 0,
        Recipients = RecipientPolicy.PerRecipient,
        Headers = HeaderPolicy.DropAll,
        ReplyTo = ReplyToStyle.NotSupported,
        ReplyToHint = "Set EmailSender:GovUkNotify:ReplyToId instead.",
        Tracking = TrackingStyle.NotApplicable,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.None,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var notify = options.GovUkNotify;
        var (serviceId, secret) = Key(notify);
        var personalisation = new NotifyPersonalisation(
            email.Subject,
            email.TextBody is { } text ? NotifyBodyFormatter.FromText(text) : NotifyBodyFormatter.ToText(email.HtmlBody));
        var replyToId = string.IsNullOrWhiteSpace(notify.ReplyToId) ? null : notify.ReplyToId.Trim();

        // The per-recipient policy hands over exactly one To.
        var body = new NotifyRequest(email.To[0], notify.EmailTemplateId!.Trim(), personalisation, replyToId);
        var request = new HttpRequestMessage(HttpMethod.Post, Host + "v2/notifications/email")
        {
            Content = JsonContent.Create(body, NotifyJsonContext.Default.NotifyRequest),
        };
        // A fresh token per attempt: Notify refuses one older than 30 seconds.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(serviceId, secret));
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            var errorClass = FirstError(json, "error") is { } c && ErrorClass().IsMatch(c) ? c : null;
            // TooManyRequestsError is the daily send limit [recalled; docs/spikes.md M6]: waiting seconds will not clear it.
            return ProviderOutcome.ForStatus(response.StatusCode, errorClass, Hint(response.StatusCode, json), quotaStop: errorClass == "TooManyRequestsError");
        }

        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        return ResponseReader.String(json.RootElement, "id") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
    }

    public HttpRequestMessage? BuildHealthRequest(EmailSenderOptions options)
    {
        var notify = options.GovUkNotify;
        if (!NotifyApiKey.TryParse(notify.ApiKey, out var serviceId, out var secret) || !Guid.TryParse(notify.EmailTemplateId?.Trim(), out var templateId))
        {
            throw new EmailSendException("EmailSender:GovUkNotify:ApiKey or EmailTemplateId is not valid.");
        }

        // Reading the template proves the key and the template id without sending anything.
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Host}v2/template/{templateId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(serviceId, secret));
        return request;
    }

    public HealthCheckStatus ReadHealth(HttpResponseMessage response, ReadOnlyMemory<byte> body) => response.StatusCode switch
    {
        HttpStatusCode.OK => CheckTemplate(body),
        HttpStatusCode.Forbidden => Error($"{Name} rejected the API key. Check EmailSender:GovUkNotify:ApiKey."),
        HttpStatusCode.NotFound => Error($"{Name} has no email template with the configured EmailTemplateId."),
        _ => Error($"{Name} answered HTTP {(int)response.StatusCode}."),
    };

    private static (string ServiceId, string Secret) Key(NotifyOptions notify)
        => NotifyApiKey.TryParse(notify.ApiKey, out var serviceId, out var secret)
            ? (serviceId, secret)
            : throw new EmailSendException("EmailSender:GovUkNotify:ApiKey is not a GOV.UK Notify API key.");

    // The template is configuration, not personal data, but only these three fields are ever read.
    private HealthCheckStatus CheckTemplate(ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        string? Member(string name) => json is null ? null : ResponseReader.String(json.RootElement, name);
        var isEmail = string.Equals(Member("type"), "email", StringComparison.OrdinalIgnoreCase);
        var hasSubject = Member("subject")?.Contains("((subject))", StringComparison.OrdinalIgnoreCase) == true;
        var hasBody = Member("body")?.Contains("((body))", StringComparison.OrdinalIgnoreCase) == true;
        return isEmail && hasSubject && hasBody
            ? new HealthCheckStatus($"{Name} accepted the API key and the email template.") { ResultType = StatusResultType.Success }
            : new HealthCheckStatus($"The {Name} template must be an email template with the subject ((subject)) and the body ((body)); otherwise emails arrive with the wrong content.") { ResultType = StatusResultType.Warning };
    }

    private static HealthCheckStatus Error(string text) => new(text) { ResultType = StatusResultType.Error };

    // Fixed text only; the message is read just to choose it and is never echoed.
    private static string? Hint(HttpStatusCode status, JsonDocument? json) => status switch
    {
        HttpStatusCode.Forbidden => "Check EmailSender:GovUkNotify:ApiKey.",
        HttpStatusCode.BadRequest when FirstError(json, "message") is { } m
            && (m.Contains("team-only", StringComparison.OrdinalIgnoreCase) || m.Contains("trial mode", StringComparison.OrdinalIgnoreCase))
            => "The API key is team-only or the service is in trial mode, so it cannot send to this recipient.",
        _ => null,
    };

    // HS256 by hand: Notify's token is three base64url parts, and a JWT library would be one more dependency for 15 lines.
    private string CreateToken(string serviceId, string secret)
    {
        var header = Base64Url(Encoding.ASCII.GetBytes("""{"typ":"JWT","alg":"HS256"}"""));
        var claims = Base64Url(Encoding.ASCII.GetBytes($$"""{"iss":"{{serviceId}}","iat":{{clock.GetUtcNow().ToUnixTimeSeconds()}}}"""));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{header}.{claims}"));
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Notify answers {"errors":[{"error":"BadRequestError","message":"..."}],"status_code":400}.
    private static string? FirstError(JsonDocument? json, string name)
        => json?.RootElement is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty("errors", out var errors)
           && errors is { ValueKind: JsonValueKind.Array }
           && errors.GetArrayLength() > 0
           && errors[0] is { ValueKind: JsonValueKind.Object } first
            ? ResponseReader.String(first, name)
            : null;

    [GeneratedRegex("^[A-Za-z]{1,64}$", RegexOptions.None, 1000)]
    private static partial Regex ErrorClass();
}
