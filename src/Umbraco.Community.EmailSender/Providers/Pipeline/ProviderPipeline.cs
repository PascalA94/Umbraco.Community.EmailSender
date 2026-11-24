using System.Net;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>
/// One logical send to one provider: header policy, size check, attempts, retry and logging (design 9.2). The budget,
/// <c>EmailSender:TimeoutSeconds</c>, covers every attempt and every wait.
/// </summary>
internal sealed partial class ProviderPipeline(
    IProviderMapper mapper,
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ILogger logger,
    Func<double> jitter)
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// Umbraco sends invites and resets on the request thread, so a longer wait would hang the backoffice: fail instead.
    /// </summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

    private ProviderCapabilities Caps => mapper.Capabilities;

    /// <summary>Sends once, with retries where design 9.2 allows them, and logs the outcome.</summary>
    /// <exception cref="EmailSendException">Refused, rate limited, unreadable where that means failure, or out of budget.</exception>
    public async Task SendAsync(OutboundEmail email, EmailSenderOptions options, CancellationToken ct)
    {
        email = ApplyPolicies(email);
        var started = clock.GetTimestamp();
        var budget = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var key = Guid.NewGuid().ToString("D");
        using var timeout = new CancellationTokenSource(budget, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;
        var refreshed = false;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                // Inside the budget: a slow token endpoint cannot hang the request thread.
                var current = new SendAttempt(attempt, key, await mapper.AcquireCredentialAsync(options, token));
                using var request = await PrepareAsync(email, options, current, token);
                HttpResponseMessage response;
                try
                {
                    response = await httpClientFactory.CreateClient(mapper.ClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                }
                catch (HttpRequestException) when (!token.IsCancellationRequested)
                {
                    // The request may have reached the provider before the connection broke, so it is repeated only when an
                    // idempotency key makes a repeat safe.
                    var failure = $"{mapper.Name} send failed: {nameof(HttpRequestException)}.";
                    if (Caps.Idempotency == IdempotencyCarriage.None || attempt >= MaxAttempts
                        || !CanWait(Backoff(attempt, jitter), started, budget))
                    {
                        throw Fail(failure, "connection failure");
                    }

                    await WaitAsync(Backoff(attempt, jitter), token);
                    continue;
                }

                using (response)
                {
                    var body = await ResponseReader.ReadCappedAsync(response, token);
                    switch (mapper.ReadOutcome(response, body, current))
                    {
                        case ProviderOutcome.AcceptedOutcome accepted:
                            LogAccepted(email, accepted, attempt, started);
                            return;
                        case ProviderOutcome.UnreadableOutcome when response.IsSuccessStatusCode && !Caps.FailClosedOn2xx:
                            LogAccepted(email, (ProviderOutcome.AcceptedOutcome)ProviderOutcome.Accepted([]), attempt, started);
                            return;
                        case ProviderOutcome.UnreadableOutcome when response.IsSuccessStatusCode:
                            throw Fail($"{mapper.Name} answered HTTP {(int)response.StatusCode} but the result could not be read.", (int)response.StatusCode, null);
                        case ProviderOutcome.RejectedOutcome { Status: 401 } when mapper.RefreshesCredentialOnUnauthorized && !refreshed && attempt < MaxAttempts:
                            // An expired credential, not a refused send: nothing was accepted, so repeating is safe. Once only; a
                            // second 401 falls through to the refusal below.
                            refreshed = true;
                            mapper.ForgetCredential();
                            break;
                        case ProviderOutcome.RejectedOutcome rejected:
                            var wait = DecideRetry(rejected, response, attempt, started, budget);
                            await WaitAsync(wait, token);
                            break;
                        default:
                            throw Fail(RejectionText(mapper.Name, Caps.ErrorCodeLabel, (ProviderOutcome.RejectedOutcome)ProviderOutcome.ForStatus(response.StatusCode, null, null)), (int)response.StatusCode, null);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw Fail($"{mapper.Name} did not answer within {options.TimeoutSeconds} seconds.", "timeout");
        }
    }

    /// <summary>
    /// The design 9.2 retry table. Retry only when the provider certainly did not accept the email (429, 420, 503), or when a
    /// documented idempotency key makes a repeat of 500, 502 or 504 safe: a duplicate invite is worse than a failed one. This
    /// is why ACS, which has no key, keeps SDK retries off (M2 ruling) and never comes through here.
    /// </summary>
    private TimeSpan DecideRetry(
        ProviderOutcome.RejectedOutcome rejected,
        HttpResponseMessage response,
        int attempt,
        long started,
        TimeSpan budget)
    {
        var rateLimited = rejected.Status is 429 or 420 && !rejected.QuotaStop;
        var text = rateLimited
            ? $"{mapper.Name} is rate limiting this site (HTTP {rejected.Status}). Try again later."
            : RejectionText(mapper.Name, Caps.ErrorCodeLabel, rejected);
        // The table is enforced here, not trusted from the mapper: a mapper's flag can only narrow a retry, never widen one.
        var retryable = rejected.Retryable
            && ((rejected.Status is 429 or 420 or 503 && !rejected.QuotaStop)
                || (rejected.Status is 500 or 502 or 504 && Caps.Idempotency != IdempotencyCarriage.None));
        if (!retryable || attempt >= MaxAttempts)
        {
            throw Fail(text, rejected.Status, rejected.Code);
        }

        var wait = RetryAfter(response) ?? Backoff(attempt, jitter);
        if (!CanWait(wait, started, budget))
        {
            throw Fail(text, rejected.Status, rejected.Code);
        }

        return wait;
    }

    public static string RejectionText(string name, string? codeLabel, ProviderOutcome.RejectedOutcome rejected)
    {
        var code = ResponseReader.SafeCode(rejected.Code);
        var codePart = code is null ? string.Empty : codeLabel is null ? $", {code}" : $", {codeLabel} {code}";
        var hint = string.IsNullOrWhiteSpace(rejected.Hint) ? string.Empty : $" {rejected.Hint}";
        return $"{name} rejected the email: HTTP {rejected.Status}{codePart}.{hint}";
    }

    /// <summary>1 s, then 2 s, each plus or minus 20%.</summary>
    public static TimeSpan Backoff(int attempt, Func<double> random)
        => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * (0.8 + (0.4 * random())));

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        // Other rate-limit headers differ per provider and are partly undocumented, so only Retry-After is read (design 9.11).
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - clock.GetUtcNow();
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private bool CanWait(TimeSpan wait, long started, TimeSpan budget)
        => wait <= MaxWait && clock.GetElapsedTime(started) + wait < budget;

    private Task WaitAsync(TimeSpan wait, CancellationToken ct)
        => wait <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(wait, clock, ct);

    private OutboundEmail ApplyPolicies(OutboundEmail email)
    {
        var headers = Caps.Headers switch
        {
            HeaderPolicy.DropAll => new Dictionary<string, string>(),
            HeaderPolicy.XPrefixedOnly => email.Headers
                .Where(h => h.Key.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value),
            _ => email.Headers,
        };
        if (headers.Count < email.Headers.Count)
        {
            LogHeadersDropped(mapper.Name, email.Headers.Count - headers.Count);
        }

        IReadOnlyList<string> replyTo = email.ReplyTo;
        if (email.ReplyTo.Count > 0 && Caps.ReplyTo == ReplyToStyle.NotSupported)
        {
            // An invite never fails over a Reply-To.
            LogReplyToIgnored(mapper.Name, Caps.ReplyToHint ?? string.Empty);
            replyTo = [];
        }
        else if (email.ReplyTo.Count > 1 && Caps.ReplyTo == ReplyToStyle.Single)
        {
            LogReplyToTrimmed(mapper.Name, email.ReplyTo.Count - 1);
            replyTo = [email.ReplyTo[0]];
        }

        return email with { Headers = headers, ReplyTo = replyTo };
    }

    /// <summary>
    /// Builds the attempt's request and serialises its body to bytes, so the size check is exact and happens before anything
    /// is sent (the body is buffered anyway).
    /// </summary>
    private async Task<HttpRequestMessage> PrepareAsync(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt, CancellationToken ct)
    {
        var request = mapper.BuildRequest(email, options, attempt);
        if (Caps.AllowedHosts is { } hosts
            && (request.RequestUri is not { IsAbsoluteUri: true, Scheme: "https" } uri
                || !hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)))
        {
            request.Dispose();
            throw Fail($"{mapper.Name} built a request for a host it is not allowed to use. Nothing was sent.", "host not allowed");
        }

        if (request.Content is null)
        {
            return request;
        }

        var bytes = await request.Content.ReadAsByteArrayAsync(ct);
        if (Caps.MaxRequestBytes is { } max && bytes.Length > max)
        {
            request.Dispose();
            throw new EmailSendException(
                $"The email is larger than {mapper.Name} accepts in one request ({ProviderPreChecks.FormatSize(max)} after encoding). Remove or shrink the attachments.");
        }

        var buffered = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
        {
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content.Dispose();
        request.Content = buffered;
        return request;
    }

    private void LogAccepted(OutboundEmail email, ProviderOutcome.AcceptedOutcome accepted, int attempts, long started)
    {
        if (accepted.Warning is { } warning)
        {
            LogAcceptedWarning(mapper.Name, warning);
        }

        LogSent(
            mapper.Name,
            email.To.Count + email.Cc.Count + email.Bcc.Count,
            email.Attachments.Count,
            accepted.Ids.Count > 0 ? ResponseReader.SafeId(accepted.Ids[0]) ?? "unreadable" : "none",
            attempts,
            (long)clock.GetElapsedTime(started).TotalMilliseconds);
    }

    private EmailSendException Fail(string text, int status, string? code)
    {
        LogRejected(mapper.Name, status, ResponseReader.SafeCode(code) ?? "none");
        return new EmailSendException(text);
    }

    private EmailSendException Fail(string text, string reason)
    {
        LogFailed(mapper.Name, reason);
        return new EmailSendException(text);
    }

    // Never an address, subject, body, key, token or response text in any of these (design 9.2 step 7).
    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} accepted the email: {Recipients} recipients, {Attachments} attachments, message id {MessageId}, {Attempts} attempts, {ElapsedMs} ms")]
    private partial void LogSent(string provider, int recipients, int attachments, string messageId, int attempts, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} send failed: HTTP {Status}, code {Code}")]
    private partial void LogRejected(string provider, int status, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} send failed: {Reason}")]
    private partial void LogFailed(string provider, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider}: {Warning}")]
    private partial void LogAcceptedWarning(string provider, string warning);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Provider} does not take these custom headers; {Count} not sent")]
    private partial void LogHeadersDropped(string provider, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} cannot use a per-message Reply-To, so it was ignored. {Hint}")]
    private partial void LogReplyToIgnored(string provider, string hint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} takes one Reply-To address; {Dropped} more were not sent")]
    private partial void LogReplyToTrimmed(string provider, int dropped);
}
