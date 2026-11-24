using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Mandrill;

/// <summary>
/// Mandrill's (Mailchimp Transactional) <c>POST /api/1.4/messages/send</c> (design 9.2 and 9.3). The key is a body field, so
/// there is no auth header to redact and the request body is the one place a secret travels. Mandrill answers 200 with one
/// result per recipient: any recipient that is not sent, queued or scheduled makes the whole send a failure. Errors match on the
/// <c>name</c>, never the numeric <c>code</c>, which differs between the reference and older clients.
/// </summary>
internal sealed class MandrillMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.mandrill";

    /// <summary>The documented <c>reject_reason</c> values. Anything else is dropped: the text could be anything.</summary>
    private static readonly HashSet<string> RejectReasons =
    [
        "hard-bounce", "soft-bounce", "spam", "unsub", "custom", "invalid-sender", "invalid", "test-mode-limit", "unsigned", "rule",
    ];

    public string Name => "Mandrill";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["mandrillapp.com"],
        // The JSON for one call must stay under 10 MB [docs].
        MaxRequestBytes = 10 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        // No dedicated field: Reply-To is a header inside the message.
        ReplyTo = ReplyToStyle.CustomHeader,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
        // A failure comes back inside a 200, per recipient.
        FailClosedOn2xx = true,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var recipients = new List<MandrillRecipient>();
        recipients.AddRange(email.To.Select(a => new MandrillRecipient(a, "to")));
        recipients.AddRange(email.Cc.Select(a => new MandrillRecipient(a, "cc")));
        recipients.AddRange(email.Bcc.Select(a => new MandrillRecipient(a, "bcc")));

        var headers = new Dictionary<string, string>(email.Headers);
        if (email.ReplyTo.Count > 0)
        {
            headers["Reply-To"] = string.Join(", ", email.ReplyTo);
        }

        var track = options.DisableLinkTracking ? (bool?)false : null;
        var message = new MandrillMessage(
            email.FromAddress,
            email.FromName,
            recipients,
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            headers.Count == 0 ? null : headers,
            // Every attachment is a normal attachment; images[] is never used because there is no content id to give.
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new MandrillAttachment(a.ContentType, a.Name, a.Base64))],
            track,
            track);

        return new HttpRequestMessage(HttpMethod.Post, "https://mandrillapp.com/api/1.4/messages/send")
        {
            Content = JsonContent.Create(new MandrillRequest(options.Mandrill.ApiKey!, message), MandrillJsonContext.Default.MandrillRequest),
        };
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (!response.IsSuccessStatusCode)
        {
            // {"status":"error","code":<int>,"name":"...","message":"..."}: only the name is read.
            var name = json is null ? null : ResponseReader.String(json.RootElement, "name");
            return ProviderOutcome.ForStatus(response.StatusCode, name, Hint(response.StatusCode, name));
        }

        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
        {
            return ProviderOutcome.Unreadable;
        }

        var ids = new List<string>();
        var total = 0;
        var rejected = 0;
        string? reason = null;
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            total++;
            var status = entry.ValueKind == JsonValueKind.Object ? ResponseReader.String(entry, "status") : null;
            // queued is not a failure (an exhausted hourly quota or an attachment queues the message), and neither is scheduled.
            if (status is "sent" or "queued" or "scheduled")
            {
                if (ResponseReader.String(entry, "_id") is { } id)
                {
                    ids.Add(id);
                }

                continue;
            }

            rejected++;
            if (reason is null && entry.ValueKind == JsonValueKind.Object && ResponseReader.String(entry, "reject_reason") is { } candidate && RejectReasons.Contains(candidate))
            {
                reason = candidate;
            }
        }

        if (rejected == 0)
        {
            return ProviderOutcome.Accepted(ids);
        }

        var text = reason is null
            ? $"{rejected} of {total} recipients rejected."
            : $"{rejected} of {total} recipients rejected (reject_reason: {reason}).";
        // Mail from an unverified domain is not an HTTP error: it comes back rejected with reject_reason unsigned.
        if (reason == "unsigned")
        {
            text += " Verify the sender domain in Mandrill: it needs SPF and DKIM.";
        }

        return ProviderOutcome.Rejected((int)response.StatusCode, null, text, retryable: false, quotaStop: false);
    }

    private static string? Hint(HttpStatusCode status, string? name) => (status, name) switch
    {
        (HttpStatusCode.Unauthorized, _) or (_, "Invalid_Key") => "Check EmailSender:Mandrill:ApiKey.",
        (HttpStatusCode.PaymentRequired, _) or (_, "PaymentRequired") => "Check the Mandrill account's plan and sending blocks.",
        _ => null,
    };
}
