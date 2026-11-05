using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

/// <summary>
/// Sends through Postmark's <c>POST /email</c>. A singleton over <c>IHttpClientFactory</c> and <c>IOptionsMonitor</c>
/// (pitfalls P1 and P2). Failures carry the HTTP status and Postmark's numeric ErrorCode only: never the response text.
/// </summary>
internal sealed class PostmarkProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<EmailSenderOptions> options,
    TimeProvider clock,
    ILogger<PostmarkProvider> logger) : IEmailProvider
{
    public const string ClientName = "community.emailSender.postmark";

    private const string TokenHeader = "X-Postmark-Server-Token";
    private const int MaxRecipients = 50; // Postmark counts To, Cc and Bcc together.
    private const int MaxResponseBytes = 4096;

    public string Name => "Postmark";

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        try
        {
            await SendCoreAsync(email, ct);
        }
        catch (Exception ex) when (ex is not (EmailSendException or OperationCanceledException))
        {
            // The type name only: HttpRequestException and FormatException text can echo the request or the token.
            throw new EmailSendException($"{Name} send failed: {ex.GetType().Name}.");
        }
    }

    private async Task SendCoreAsync(OutboundEmail email, CancellationToken ct)
    {
        var current = options.CurrentValue;
        if (email.To.Count + email.Cc.Count + email.Bcc.Count > MaxRecipients)
        {
            throw new EmailSendException($"Postmark accepts at most {MaxRecipients} recipients in To, Cc and Bcc together.");
        }

        var body = new PostmarkRequest(
            email.From,
            string.Join(',', email.To),
            Join(email.Cc),
            Join(email.Bcc),
            Join(email.ReplyTo),
            email.Subject,
            email.HtmlBody,
            email.TextBody,
            current.Postmark.MessageStream,
            current.DisableLinkTracking ? false : null,
            current.DisableLinkTracking ? "None" : null,
            email.Headers.Count == 0 ? null : [.. email.Headers.Select(h => new PostmarkHeader(h.Key, h.Value))],
            email.Attachments.Count == 0 ? null : [.. email.Attachments.Select(a => new PostmarkAttachment(a.Name, a.Base64, a.ContentType))]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "email")
        {
            Content = JsonContent.Create(body, PostmarkJsonContext.Default.PostmarkRequest),
        };
        request.Headers.Add(TokenHeader, current.Postmark.ServerToken);

        var messageId = await WithTimeoutAsync(current, ct, async token =>
        {
            using var response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var json = await ReadCappedAsync(response, token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var code = IntMember(json, "ErrorCode");
                var hint = response.StatusCode == HttpStatusCode.Unauthorized ? " Check EmailSender:Postmark:ServerToken." : string.Empty;
                throw new EmailSendException(
                    $"Postmark rejected the email: HTTP {(int)response.StatusCode}{(code is { } c ? $", ErrorCode {c}" : string.Empty)}.{hint}");
            }

            return Guid.TryParse(StringMember(json, "MessageID"), out var id) ? id : (Guid?)null;
        });

        if (messageId is { } accepted)
        {
            logger.LogInformation("Postmark accepted message {MessageId}", accepted);
        }
    }

    public async Task<HealthCheckStatus> CheckAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        try
        {
            return await WithTimeoutAsync(current, ct, async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "server");
                request.Headers.Add(TokenHeader, current.Postmark.ServerToken);
                using var response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => new HealthCheckStatus("Postmark accepted the server token.") { ResultType = StatusResultType.Success },
                    HttpStatusCode.Unauthorized => new HealthCheckStatus("Postmark rejected the server token.") { ResultType = StatusResultType.Error },
                    _ => new HealthCheckStatus($"Postmark answered HTTP {(int)response.StatusCode}.") { ResultType = StatusResultType.Error },
                };
            });
        }
        catch (EmailSendException ex)
        {
            return new HealthCheckStatus(ex.Message) { ResultType = StatusResultType.Error };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type name only: HttpRequestException text can echo the request.
            return new HealthCheckStatus($"Postmark could not be reached ({ex.GetType().Name}).") { ResultType = StatusResultType.Error };
        }
    }

    private static string? Join(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join(',', values);

    private Task<T> WithTimeoutAsync<T>(EmailSenderOptions current, CancellationToken ct, Func<CancellationToken, Task<T>> work)
        => ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct, work);

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxResponseBytes];
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

    // Only a numeric ErrorCode and a MessageID are ever read from a response; a malformed body yields nothing.
    private static int? IntMember(byte[] json, string name)
        => Member(json, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

    private static string? StringMember(byte[] json, string name)
        => Member(json, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static JsonElement? Member(byte[] json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value)
                ? value.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
