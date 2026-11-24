using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Smtp2Go;

/// <summary>
/// SMTP2GO's <c>POST /v3/email/send</c> (design 9.2 and 9.3). It answers 200 even when sending failed, so a 2xx is a failure when
/// <c>data.failed</c> is above zero or <c>data.error_code</c> is present, and an unreadable one fails closed. Both documented
/// error shapes (nested under <c>data</c>, and flat) are read; only the error code is ever echoed.
/// </summary>
internal sealed class Smtp2GoMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.smtp2go";

    /// <summary>Region to host. The global host picks the nearest region by the caller's DNS resolver, so residency needs a regional value.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Regions = new Dictionary<string, string>
    {
        ["Global"] = "api.smtp2go.com",
        ["US"] = "us-api.smtp2go.com",
        ["EU"] = "eu-api.smtp2go.com",
        ["AU"] = "au-api.smtp2go.com",
    };

    public string Name => "SMTP2GO";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = [.. Regions.Values],
        MaxRequestBytes = 50 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        // No Reply-To field: the address goes in custom_headers.
        ReplyTo = ReplyToStyle.CustomHeader,
        // A setting on the API key, not on the request.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.None,
        // The content id of an inline image is its file name.
        Inline = InlineContentId.FileNameOnly,
        FailClosedOn2xx = true,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        // Start-up validation already ran, but options can reload: the region becomes the host.
        var region = ProviderValidation.MatchRegion(options.Smtp2Go.Region, [.. Regions.Keys])
                     ?? throw new EmailSendException($"EmailSender:Smtp2Go:Region must be one of: {string.Join(", ", Regions.Keys)}.");

        var headers = new List<Smtp2GoHeader>();
        if (email.ReplyTo.Count > 0)
        {
            headers.Add(new Smtp2GoHeader("Reply-To", string.Join(", ", email.ReplyTo)));
        }

        headers.AddRange(email.Headers.Select(h => new Smtp2GoHeader(h.Key, h.Value)));

        var body = new Smtp2GoRequest(
            email.From,
            email.To,
            email.Cc.Count == 0 ? null : [.. email.Cc],
            email.Bcc.Count == 0 ? null : [.. email.Bcc],
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            headers.Count == 0 ? null : headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new Smtp2GoAttachment(a.Name, a.Base64, a.ContentType))]);

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Regions[region]}/v3/email/send")
        {
            Content = JsonContent.Create(body, Smtp2GoJsonContext.Default.Smtp2GoRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Add("X-Smtp2go-Api-Key", options.Smtp2Go.ApiKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        var status = (int)response.StatusCode;
        if (json is null)
        {
            return response.IsSuccessStatusCode
                ? ProviderOutcome.Unreadable
                : ProviderOutcome.ForStatus(response.StatusCode, null, Hint(response.StatusCode));
        }

        var root = json.RootElement;
        // The documented shapes differ: nested under data (OpenAPI) and flat (the response-codes page). Read both.
        var code = ResponseReader.String(root, "data", "error_code") ?? ResponseReader.String(root, "error_code");
        if (!response.IsSuccessStatusCode)
        {
            return ProviderOutcome.ForStatus(response.StatusCode, code, Hint(response.StatusCode));
        }

        if (code is not null)
        {
            return ProviderOutcome.Rejected(status, code, Hint(response.StatusCode), retryable: false, quotaStop: false);
        }

        if (ResponseReader.Int(root, "data", "failed") is > 0 and var failed)
        {
            var succeeded = ResponseReader.Int(root, "data", "succeeded") ?? 0;
            var hint = UnverifiedSender(root) ? "Verify the sender domain or address in SMTP2GO." : null;
            // A resend of the whole email would reach the accepted recipients twice, so say they were accepted.
            if (succeeded > 0)
            {
                hint = $"{failed} of {failed + succeeded} recipients failed. The other {succeeded} {(succeeded == 1 ? "was" : "were")} accepted: do not resend to them.";
            }

            return ProviderOutcome.Rejected(status, null, hint, retryable: false, quotaStop: false);
        }

        // A 200 is not proof of acceptance: it needs positive evidence (an id, or a success count), or it fails closed.
        var emailId = ResponseReader.String(root, "data", "email_id");
        return emailId is { Length: > 0 } || ResponseReader.Int(root, "data", "succeeded") is > 0
            ? ProviderOutcome.Accepted(emailId is { Length: > 0 } ? [emailId] : [])
            : ProviderOutcome.Unreadable;
    }

    private static string? Hint(HttpStatusCode status) => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        ? "Check EmailSender:Smtp2Go:ApiKey."
        : null;

    // The failure text is free and can echo addresses, so it is only searched, never copied. The unverified-sender answer is
    // undocumented [unknown]; the help centre shows the text "sender domain not verified".
    private static bool UnverifiedSender(JsonElement root)
        => ResponseReader.Walk(root, "data", "failures") is { ValueKind: JsonValueKind.Array } failures
           && failures.EnumerateArray().Any(f => f.ValueKind == JsonValueKind.String && f.GetString()!.Contains("not verified", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> StartupWarnings(EmailSenderOptions options)
        => options.Smtp2Go.ApiKey is { Length: > 0 } key && !key.StartsWith("api-", StringComparison.Ordinal)
            ? ["EmailSender:Smtp2Go:ApiKey does not start with api-, which every SMTP2GO API key does. Check that the key is an API key."]
            : [];
}
