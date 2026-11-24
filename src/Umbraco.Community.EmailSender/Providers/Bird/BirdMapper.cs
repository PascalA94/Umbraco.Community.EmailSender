using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Bird;

/// <summary>
/// Bird's <c>POST /v1/email/messages</c> (design 9.2 and 9.3). Every send is <c>category: "transactional"</c>, because Bird
/// defaults an inline send to marketing, which blocks on every suppression and opt-out. The tracking flags default to true on
/// Bird's side, so false is sent explicitly. Only the error <c>code</c> and <c>type</c> of a failure are read; the message can
/// echo addresses.
/// </summary>
internal sealed class BirdMapper : IProviderMapper
{
    public const string ClientName = "community.emailSender.bird";

    internal static readonly string[] Regions = ["us1", "eu1"];

    public string Name => "Bird";

    string IProviderMapper.ClientName => ClientName;

    public ProviderCapabilities Capabilities { get; } = new()
    {
        AllowedHosts = [.. Regions.Select(Host)],
        MaxAttachments = 20,
        // The generated message is capped at 20 MB after base64 and MIME, and so is the whole request body [docs].
        MaxRequestBytes = 20 * 1024 * 1024,
        Headers = HeaderPolicy.SendAsGiven,
        ReplyTo = ReplyToStyle.List,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.Header,
        Inline = InlineContentId.ContentId,
    };

    private static string Host(string region) => $"{region}.platform.bird.com";

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        // Start-up validation already ran, but options can reload: the region becomes part of the host.
        var region = ProviderValidation.MatchRegion(options.Bird.Region, Regions)
                     ?? throw new EmailSendException($"EmailSender:Bird:Region must be one of: {string.Join(", ", Regions)}.");

        var track = options.DisableLinkTracking ? (bool?)false : null;
        var body = new BirdRequest(
            new BirdAddress(email.FromAddress, email.FromName),
            email.To,
            NullIfEmpty(email.Cc),
            NullIfEmpty(email.Bcc),
            NullIfEmpty(email.ReplyTo),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            email.Headers.Count == 0 ? null : email.Headers,
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new BirdAttachment(a.Name, a.Base64, a.ContentType))],
            "transactional",
            track,
            track);

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host(region)}/v1/email/messages")
        {
            Content = JsonContent.Create(body, BirdJsonContext.Default.BirdRequest),
        };
        // A key with CR or LF throws FormatException here; the provider wraps it by type name, so the key is never echoed.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Bird.ApiKey);
        request.Headers.Add("Idempotency-Key", attempt.IdempotencyKey);
        return request;
    }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (response.IsSuccessStatusCode)
        {
            // 202 means accepted; delivery failures arrive later as events (design 9.2).
            if (json is null)
            {
                return ProviderOutcome.Unreadable;
            }

            return ResponseReader.String(json.RootElement, "id") is { } id ? ProviderOutcome.Accepted([id]) : ProviderOutcome.Accepted([]);
        }

        var code = json is null ? null : ResponseReader.String(json.RootElement, "error", "code");
        var type = json is null ? null : ResponseReader.String(json.RootElement, "error", "type");
        return ProviderOutcome.ForStatus(response.StatusCode, code, Hint(response.StatusCode, type));
    }

    // Bird answers 422 both for a bad field (validation_error) and for an unverified sender domain (a delivery or business-rule
    // type), so only the type tells the two apart.
    private static string? Hint(HttpStatusCode status, string? type) => (int)status switch
    {
        401 or 403 => "Check EmailSender:Bird:ApiKey.",
        421 => "The key's region does not match EmailSender:Bird:Region.",
        422 when type is "delivery_error" => "Verify the sender domain in Bird. The shared onboarding domain delivers only to workspace members and @messagebird.dev addresses.",
        _ => null,
    };

    public IReadOnlyList<string> StartupWarnings(EmailSenderOptions options)
    {
        var key = options.Bird.ApiKey;
        var region = ProviderValidation.MatchRegion(options.Bird.Region, Regions);
        if (string.IsNullOrEmpty(key) || region is null)
        {
            return [];
        }

        // A workspace key is bk_<region>_..., an organisation key bm_<region>_...; the wrong region is answered with 421.
        return key.StartsWith($"bk_{region}_", StringComparison.Ordinal) || key.StartsWith($"bm_{region}_", StringComparison.Ordinal)
            ? []
            : ["EmailSender:Bird:ApiKey does not start with bk_<region>_ or bm_<region>_ for the region set in EmailSender:Bird:Region. Bird answers 421 when they differ."];
    }

    private static List<string>? NullIfEmpty(IReadOnlyList<string> values) => values.Count == 0 ? null : [.. values];
}
