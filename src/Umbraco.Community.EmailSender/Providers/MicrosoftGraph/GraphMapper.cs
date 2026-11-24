using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

/// <summary>
/// Microsoft Graph <c>POST /users/{id}/sendMail</c> with an app-only token (design 9.4). The mailbox in
/// <c>SenderUserId</c> is the sender, so no From is sent. The token comes from <see cref="GraphTokenSource"/> before each
/// attempt; a 401 refreshes it once. Graph returns no message id, and has no idempotency key, so a 5xx is never retried.
/// </summary>
internal sealed class GraphMapper(GraphTokenSource tokens) : IProviderMapper
{
    public const string ClientName = "community.emailSender.graph";

    private const string Host = "https://graph.microsoft.com/v1.0/users/";

    public string Name => "Microsoft Graph";

    string IProviderMapper.ClientName => ClientName;

    // The mailbox decides the sender, and a Forms or Smtp:From address would be refused or ignored.
    public bool UsesFromAddress => false;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = ["graph.microsoft.com"],
        // The default per-message limit, admin-adjustable [docs].
        MaxRequestBytes = 35L * 1024 * 1024,
        // The documented limit of an attachment added in one call; whether sendMail takes more inline is [unknown].
        MaxAttachmentBytesEach = 3L * 1024 * 1024,
        // internetMessageHeaders names must start with x- [docs], so core's Expires is dropped.
        Headers = HeaderPolicy.XPrefixedOnly,
        ReplyTo = ReplyToStyle.List,
        Tracking = TrackingStyle.NotApplicable,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.ContentId,
    };

    public bool RefreshesCredentialOnUnauthorized => true;

    public async ValueTask<string?> AcquireCredentialAsync(EmailSenderOptions options, CancellationToken ct)
        => await tokens.GetAsync(ct);

    public void ForgetCredential() => tokens.Invalidate();

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        var graph = options.MicrosoftGraph;
        // A path segment: checked again here because a reloaded setting is not validated, and ".." would change the path.
        if (graph.SenderUserId is not { } sender || !MicrosoftGraphValidation.IsSender(sender))
        {
            throw new EmailSendException(MicrosoftGraphValidation.SenderMessage);
        }

        var message = new GraphMessage(
            email.Subject,
            email.HtmlBody is { } html ? new GraphBody("HTML", html) : new GraphBody("Text", email.TextBody ?? string.Empty),
            Recipients(email.To)!,
            Recipients(email.Cc),
            Recipients(email.Bcc),
            Recipients(email.ReplyTo),
            email.Headers.Count == 0 ? null : [.. email.Headers.Select(h => new GraphHeader(h.Key, h.Value))],
            email.Attachments.Count == 0
                ? null
                : [.. email.Attachments.Select(a => new GraphAttachment("#microsoft.graph.fileAttachment", a.Name, a.ContentType, a.Base64))]);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{Host}{Uri.EscapeDataString(sender)}/sendMail")
        {
            Content = JsonContent.Create(new GraphRequest(message, graph.SaveToSentItems), GraphJsonContext.Default.GraphRequest),
        };
        // The token is opaque and fetched by the pipeline; a value with CR or LF throws FormatException here, which the provider wraps by type name.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", attempt.Credential);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        // The docs say 202 and the OpenAPI says 204: any 2xx is accepted. Acceptance is not delivery, and no id comes back.
        if (response.IsSuccessStatusCode)
        {
            return ProviderOutcome.Accepted([]);
        }

        using var json = ResponseReader.TryParse(body);
        // error.code only: the message and innerError can echo addresses.
        var code = json is null ? null : ResponseReader.String(json.RootElement, "error", "code");
        // Stricter than the shared table: Graph has no idempotency key and a 5xx (a 503 too) may follow an accepted send, so only a
        // 429 is repeated. The pipeline lets a mapper narrow a retry, never widen one.
        return ProviderOutcome.Rejected((int)response.StatusCode, code, Hint(response.StatusCode), retryable: response.StatusCode == HttpStatusCode.TooManyRequests, quotaStop: false);
    }

    public async ValueTask<HealthCheckStatus?> CheckHealthAsync(EmailSenderOptions options, CancellationToken ct)
    {
        // Mail.Send and the mailbox scope cannot be proven without sending (design 9.4).
        await tokens.GetAsync(ct);
        return new HealthCheckStatus("Microsoft Graph issued a token. Mail.Send and the mailbox scope are not proven without sending.")
        {
            ResultType = StatusResultType.Success,
        };
    }

    private static string? Hint(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "The token was rejected. Check the app's Mail.Send permission and admin consent.",
        HttpStatusCode.Forbidden => "Check the app's Mail.Send permission, admin consent and that the mailbox is inside the RBAC scope.",
        HttpStatusCode.NotFound => "Check EmailSender:MicrosoftGraph:SenderUserId: the mailbox must exist and be licensed.",
        _ => null,
    };

    private static List<GraphRecipient>? Recipients(IReadOnlyList<string> addresses)
        => addresses.Count == 0 ? null : [.. addresses.Select(a => new GraphRecipient(new GraphEmailAddress(a)))];
}
