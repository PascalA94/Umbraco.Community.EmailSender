using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

/// <summary>
/// The Entra client-credentials token for Graph (design 9.4): one cached token shared by every send, asked for again shortly
/// before it expires. A singleton over <see cref="IHttpClientFactory"/>, <see cref="IOptionsMonitor{TOptions}"/> and
/// <see cref="TimeProvider"/> (pitfalls P1 and P2); nothing is requested at start-up (P7). The token is never logged, never in
/// an exception and never parsed.
/// </summary>
internal sealed partial class GraphTokenSource : IDisposable
{
    public const string ClientName = "community.emailSender.graphToken";

    private const string Scope = "https://graph.microsoft.com/.default";
    private const string Hint = "Check EmailSender:MicrosoftGraph:TenantId, ClientId and ClientSecret.";

    /// <summary>Entra tokens last about an hour; a token is replaced once less than this remains.</summary>
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptionsMonitor<EmailSenderOptions> options;
    private readonly TimeProvider clock;
    private readonly ILogger<GraphTokenSource> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IDisposable? subscription;
    private CachedToken? cached;
    private long generation;

    public GraphTokenSource(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<EmailSenderOptions> options,
        TimeProvider clock,
        ILogger<GraphTokenSource> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
        // A new tenant, client or secret makes the cached token wrong.
        subscription = options.OnChange((_, _) => Invalidate());
    }

    /// <summary>The cached token, or one fresh request shared by every caller that arrives while it is in flight.</summary>
    /// <exception cref="EmailSendException">The settings or the token endpoint refused. The text names a status and an allow-listed code only.</exception>
    public async ValueTask<string> GetAsync(CancellationToken ct)
    {
        if (TryGetCached(out var token))
        {
            return token;
        }

        // The wait counts against the caller's budget: ct carries it.
        await gate.WaitAsync(ct);
        try
        {
            if (TryGetCached(out token))
            {
                return token;
            }

            var started = Interlocked.Read(ref generation);
            var fetched = await RequestAsync(options.CurrentValue.MicrosoftGraph, ct);
            // A settings change while the request was in flight: the entry carries the older generation, so it is used once and never served again.
            cached = fetched with { Generation = started };
            return fetched.Value;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops the cached token, for a 401 from <c>sendMail</c> or a settings change.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref generation);
        cached = null;
    }

    public void Dispose()
    {
        subscription?.Dispose();
        gate.Dispose();
    }

    private bool TryGetCached(out string token)
    {
        var current = cached;
        // An entry from before the last settings change is never served, even if it was stored after Invalidate() cleared the field.
        if (current is not null && current.Generation == Interlocked.Read(ref generation) && clock.GetUtcNow() < current.RefreshAt)
        {
            token = current.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> RequestAsync(MicrosoftGraphOptions graph, CancellationToken ct)
    {
        // The tenant is a path segment: checked here as well as at start-up, because a reloaded setting is not validated.
        if (graph.TenantId is not { } tenant || !MicrosoftGraphValidation.IsTenant(tenant))
        {
            throw new EmailSendException(MicrosoftGraphValidation.TenantMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token")
        {
            // Form-encoded, so a secret containing '&' or '=' cannot add or merge fields.
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = graph.ClientId ?? string.Empty,
                ["scope"] = Scope,
                ["client_secret"] = graph.ClientSecret ?? string.Empty,
                ["grant_type"] = "client_credentials",
            }),
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            // The type name only: the exception text can echo the request.
            throw new EmailSendException($"Microsoft Graph token request failed: {nameof(HttpRequestException)}.");
        }

        using (response)
        {
            var body = await ResponseReader.ReadCappedAsync(response, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmailSendException(FailureText((int)response.StatusCode, body));
            }

            var parsed = GraphTokenResponse.TryParse(body)
                ?? throw new EmailSendException("Microsoft Graph answered the token request but the token could not be read.");
            // There is no refresh token in this flow: ask again. The 60 s floor keeps a short token from being requested on every
            // send, and the cap at the real lifetime keeps it from outliving the token.
            var lifetime = TimeSpan.FromSeconds(parsed.ExpiresInSeconds);
            var usable = TimeSpan.FromTicks(Math.Min(lifetime.Ticks, Math.Max((lifetime - Skew).Ticks, MinimumLifetime.Ticks)));
            LogIssued(parsed.ExpiresInSeconds);
            return new CachedToken(parsed.AccessToken, clock.GetUtcNow() + usable);
        }
    }

    /// <summary>Status, the allow-listed <c>error</c> code and the numeric <c>error_codes</c> only: <c>error_description</c> carries tenant and app ids.</summary>
    private static string FailureText(int status, ReadOnlyMemory<byte> body)
    {
        string? code = null;
        var numbers = new List<int>();
        using (var json = ResponseReader.TryParse(body))
        {
            if (json is not null)
            {
                code = ResponseReader.SafeCode(ResponseReader.String(json.RootElement, "error"));
                if (ResponseReader.Walk(json.RootElement, "error_codes") is { ValueKind: JsonValueKind.Array } list)
                {
                    numbers = [.. list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _)).Select(e => e.GetInt32()).Take(3)];
                }
            }
        }

        var codePart = code is null ? string.Empty : $", {code}";
        var numberPart = numbers.Count == 0 ? string.Empty : $", error {(numbers.Count == 1 ? "code" : "codes")} {string.Join(", ", numbers)}";
        return $"Microsoft Graph token request failed: HTTP {status}{codePart}{numberPart}. {Hint}";
    }

    // The lifetime only: never the token.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Microsoft Graph issued a token that lasts {ExpiresInSeconds} s")]
    private partial void LogIssued(int expiresInSeconds);

    private sealed record CachedToken(string Value, DateTimeOffset RefreshAt, long Generation = 0)
    {
        public override string ToString() => "CachedToken";
    }
}
