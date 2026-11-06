using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

/// <summary>
/// Sends through GOV.UK Notify's <c>POST /v2/notifications/email</c> using one template with <c>((subject))</c> and
/// <c>((body))</c>. Notify takes one recipient per request and has no attachments, Cc, Bcc or free-form reply-to, so
/// those fail (or, for reply-to, are ignored with a warning) rather than silently changing who receives the email.
/// A singleton over <c>IHttpClientFactory</c> and <c>IOptionsMonitor</c> (pitfalls P1 and P2). Failures carry the HTTP
/// status and Notify's error class name only: never the response text, which can echo the recipient.
/// </summary>
internal sealed partial class NotifyProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<EmailSenderOptions> options,
    TimeProvider clock,
    ILogger<NotifyProvider> logger) : IEmailProvider
{
    public const string ClientName = "community.emailSender.govUkNotify";

    private const int MaxRecipients = 50;
    private const int MaxErrorBytes = 4096;
    private const int MaxTemplateBytes = 65536;

    public string Name => "GOV.UK Notify";

    // The sender address is fixed in the Notify service, so a From is never required or sent.
    public bool UsesFromAddress => false;

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        try
        {
            await SendCoreAsync(email, ct);
        }
        catch (Exception ex) when (ex is not (EmailSendException or OperationCanceledException))
        {
            // The type name only: HttpRequestException text can echo the request, and a JWT problem could echo the key.
            throw new EmailSendException($"{Name} send failed: {ex.GetType().Name}.");
        }
    }

    private async Task SendCoreAsync(OutboundEmail email, CancellationToken ct)
    {
        if (email.Attachments.Count > 0)
        {
            throw new EmailSendException($"{Name} does not support attachments. Remove them or use another provider.");
        }

        if (email.Cc.Count + email.Bcc.Count > 0)
        {
            throw new EmailSendException($"{Name} does not support Cc or Bcc. Send to the recipients in To instead.");
        }

        if (email.To.Count > MaxRecipients)
        {
            throw new EmailSendException($"{Name} accepts at most {MaxRecipients} recipients in To (one request each).");
        }

        var current = options.CurrentValue;
        var notify = current.GovUkNotify;
        if (!NotifyApiKey.TryParse(notify.ApiKey, out var serviceId, out var secret))
        {
            throw new EmailSendException("EmailSender:GovUkNotify:ApiKey is not a GOV.UK Notify API key.");
        }

        if (email.ReplyTo.Count > 0)
        {
            logger.LogWarning("GOV.UK Notify cannot use a per-message reply-to; set EmailSender:GovUkNotify:ReplyToId instead. The message reply-to was ignored");
        }

        var personalisation = new NotifyPersonalisation(
            email.Subject,
            email.TextBody is { } text ? NotifyBodyFormatter.FromText(text) : NotifyBodyFormatter.ToText(email.HtmlBody));
        var replyToId = string.IsNullOrWhiteSpace(notify.ReplyToId) ? null : notify.ReplyToId.Trim();

        var sent = 0;
        foreach (var recipient in email.To)
        {
            var body = new NotifyRequest(recipient, notify.EmailTemplateId!.Trim(), personalisation, replyToId);
            try
            {
                var id = await ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct, token => PostAsync(body, serviceId, secret, token));
                if (id is { } accepted)
                {
                    logger.LogInformation("GOV.UK Notify accepted notification {NotificationId}", accepted);
                }
            }
            catch (Exception ex) when (sent > 0 && ex is not OperationCanceledException)
            {
                // Earlier recipients already have the email, so the caller must know before it retries.
                var reason = ex is EmailSendException ? ex.Message : $"{Name} send failed: {ex.GetType().Name}.";
                throw new EmailSendException($"{reason} Sent to {sent} of {email.To.Count} recipients before this failure.");
            }

            sent++;
        }
    }

    private async Task<Guid?> PostAsync(NotifyRequest body, string serviceId, string secret, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/notifications/email")
        {
            Content = JsonContent.Create(body, NotifyJsonContext.Default.NotifyRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(serviceId, secret));

        using var response = await httpClientFactory.CreateClient(ClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await ReadCappedAsync(response, MaxErrorBytes, ct);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new EmailSendException(RejectionText(response.StatusCode, json));
        }

        return Guid.TryParse(StringMember(json, "id"), out var id) ? id : null;
    }

    public async Task<HealthCheckStatus> CheckAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        var notify = current.GovUkNotify;
        if (!NotifyApiKey.TryParse(notify.ApiKey, out var serviceId, out var secret) || !Guid.TryParse(notify.EmailTemplateId?.Trim(), out var templateId))
        {
            return Error("EmailSender:GovUkNotify:ApiKey or EmailTemplateId is not valid.");
        }

        try
        {
            // Reading the template proves the key and the template id without sending anything.
            return await ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct, async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/template/{templateId:D}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(serviceId, secret));
                using var response = await httpClientFactory.CreateClient(ClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => CheckTemplate(await ReadCappedAsync(response, MaxTemplateBytes, token)),
                    HttpStatusCode.Forbidden => Error($"{Name} rejected the API key. Check EmailSender:GovUkNotify:ApiKey."),
                    HttpStatusCode.NotFound => Error($"{Name} has no email template with the configured EmailTemplateId."),
                    _ => Error($"{Name} answered HTTP {(int)response.StatusCode}."),
                };
            });
        }
        catch (EmailSendException ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type name only: HttpRequestException text can echo the request.
            return Error($"{Name} could not be reached ({ex.GetType().Name}).");
        }
    }

    // The template is configuration, not personal data, but only these three fields are ever read.
    private HealthCheckStatus CheckTemplate(byte[] json)
    {
        var isEmail = string.Equals(StringMember(json, "type"), "email", StringComparison.OrdinalIgnoreCase);
        var hasSubject = StringMember(json, "subject")?.Contains("((subject))", StringComparison.OrdinalIgnoreCase) == true;
        var hasBody = StringMember(json, "body")?.Contains("((body))", StringComparison.OrdinalIgnoreCase) == true;
        return isEmail && hasSubject && hasBody
            ? new HealthCheckStatus($"{Name} accepted the API key and the email template.") { ResultType = StatusResultType.Success }
            : new HealthCheckStatus($"The {Name} template must be an email template with the subject ((subject)) and the body ((body)); otherwise emails arrive with the wrong content.") { ResultType = StatusResultType.Warning };
    }

    private static HealthCheckStatus Error(string text) => new(text) { ResultType = StatusResultType.Error };

    private string RejectionText(HttpStatusCode status, byte[] json)
    {
        var errorClass = FirstErrorMember(json, "error") is { } c && ErrorClass().IsMatch(c) ? c : null;
        var hint = status switch
        {
            HttpStatusCode.Forbidden => " Check EmailSender:GovUkNotify:ApiKey.",
            HttpStatusCode.BadRequest when FirstErrorMember(json, "message") is { } m
                && (m.Contains("team-only", StringComparison.OrdinalIgnoreCase) || m.Contains("trial mode", StringComparison.OrdinalIgnoreCase))
                => " The API key is team-only or the service is in trial mode, so it cannot send to this recipient.",
            _ => string.Empty,
        };
        return $"{Name} rejected the email: HTTP {(int)status}{(errorClass is null ? string.Empty : $", {errorClass}")}.{hint}";
    }

    // HS256 by hand: Notify's token is three base64url parts, and a JWT library would be one more dependency for 15 lines.
    private string CreateToken(string serviceId, string secret)
    {
        var header = Base64Url(Encoding.ASCII.GetBytes("""{"typ":"JWT","alg":"HS256"}"""));
        var claims = Base64Url(Encoding.ASCII.GetBytes($$"""{"iss":"{{serviceId}}","iat":{{clock.GetUtcNow().ToUnixTimeSeconds()}}}"""));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{header}.{claims}"));
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, int cap, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[cap];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), ct);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return buffer[..filled];
    }

    private static string? StringMember(byte[] json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Notify answers {"errors":[{"error":"BadRequestError","message":"..."}],"status_code":400}.
    private static string? FirstErrorMember(byte[] json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var errors)
                && errors is { ValueKind: JsonValueKind.Array }
                && errors.GetArrayLength() > 0
                && errors[0] is { ValueKind: JsonValueKind.Object } first
                && first.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z]{1,64}$", RegexOptions.None, 1000)]
    private static partial Regex ErrorClass();
}
