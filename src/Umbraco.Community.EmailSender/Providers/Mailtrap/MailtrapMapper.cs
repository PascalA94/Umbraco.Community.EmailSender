using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Mailtrap;

/// <summary>
/// Mailtrap's Email Sending <c>POST /api/send</c> on the transactional host (design 9.2 and 9.3). Never the batch or sandbox
/// hosts: the batch endpoint answers 200 even when items fail, and the sandbox delivers nothing. Errors are
/// <c>{"success":false,"errors":[...]}</c>, whose text can echo addresses, so only the status and a fixed hint are used.
/// </summary>
internal sealed class MailtrapMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.mailtrap";

    public string Name => "Mailtrap";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["send.api.mailtrap.io"],
        // Per message including attachments, on every plan by default [docs].
        MaxRequestBytes = 10 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.Single,
        // Not in the request schema [unknown]: the README says to check the domain or stream setting.
        Tracking = TrackingStyle.AccountLevel,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
    };

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var body = new MailtrapRequest(
            new MailtrapAddress(email.FromAddress, email.FromName),
            [.. email.To.Select(a => new MailtrapAddress(a))],
            email.Cc.Count == 0 ? null : [.. email.Cc.Select(a => new MailtrapAddress(a))],
            email.Bcc.Count == 0 ? null : [.. email.Bcc.Select(a => new MailtrapAddress(a))],
            email.ReplyTo.Count == 0 ? null : new MailtrapAddress(email.ReplyTo[0]),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            email.Headers.Count == 0 ? null : email.Headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new MailtrapAttachment(a.Base64, a.Name, a.ContentType, "attachment"))]);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://send.api.mailtrap.io/api/send")
        {
            Content = JsonContent.Create(body, MailtrapJsonContext.Default.MailtrapRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Mailtrap.ApiKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        if (!response.IsSuccessStatusCode)
        {
            return ProviderOutcome.ForStatus(response.StatusCode, null, Hint(response.StatusCode));
        }

        using var json = ResponseReader.TryParse(body);
        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        // Defensive: a single /api/send answers non-2xx on failure, but success:false in a 200 is never logged as sent.
        if (json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            return ProviderOutcome.Rejected((int)response.StatusCode, null, null, retryable: false, quotaStop: false);
        }

        // One id per recipient; the pipeline logs the first.
        var ids = ResponseReader.Walk(json.RootElement, "message_ids") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList()
            : [];
        return ProviderOutcome.Accepted(ids);
    }

    private static string? Hint(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Mailtrap answers 401 for a bad token and for an unverified sending domain.",
        HttpStatusCode.Forbidden => "Verify the sending domain, or check the token's permissions, in Mailtrap.",
        _ => null,
    };
}
