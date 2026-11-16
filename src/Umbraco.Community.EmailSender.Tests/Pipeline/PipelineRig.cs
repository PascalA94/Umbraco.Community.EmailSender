using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Pipeline;

/// <summary>
/// A mapper with no provider quirks: JSON body, idempotency key in a header when the capabilities say so, id from
/// <c>{"id":"..."}</c>, error code from <c>{"code":"..."}</c>. A non-JSON 2xx body is unreadable.
/// </summary>
internal sealed class FakeMapper(ProviderCapabilities capabilities) : IProviderMapper
{
    public const string Url = "https://fake.test/send";

    public string Name => "Fake";

    public string ClientName => "community.emailSender.fake";

    public ProviderCapabilities Capabilities { get; } = capabilities;

    public List<SendAttempt> Attempts { get; } = [];

    public HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt)
    {
        Attempts.Add(attempt);
        var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["from"] = email.FromAddress,
                ["to"] = email.To,
                ["subject"] = email.Subject,
                ["body"] = email.HtmlBody ?? email.TextBody,
                ["headers"] = email.Headers,
                ["attachments"] = email.Attachments.Select(a => a.Base64).ToArray(),
            }),
        };
        request.Headers.Add("X-Fake-Key", "fake-SENTINEL-KEY"); // gitleaks:allow
        if (Capabilities.Idempotency == IdempotencyCarriage.Header)
        {
            request.Headers.Add("Idempotency-Key", attempt.IdempotencyKey);
        }

        return request;
    }

    /// <summary>When set and non-null, replaces the outcome: lets a test play a careless mapper.</summary>
    public Func<HttpResponseMessage, ProviderOutcome?>? Override { get; set; }

    public ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body)
    {
        if (Override?.Invoke(response) is { } forced)
        {
            return forced;
        }

        var json = ResponseReader.TryParse(body);
        if (!response.IsSuccessStatusCode)
        {
            var code = json is null ? null : ResponseReader.String(json.RootElement, "code");
            return ProviderOutcome.ForStatus(response.StatusCode, code, null, quotaStop: code == "quota");
        }

        if (json is null)
        {
            return ProviderOutcome.Unreadable;
        }

        return ResponseReader.String(json.RootElement, "id") is { } id
            ? ProviderOutcome.Accepted([id])
            : ProviderOutcome.Accepted([]);
    }
}

internal sealed class PipelineRig
{
    public const string Address = "person.SENTINEL@example.test";
    public const string Subject = "SENTINEL-SUBJECT";
    public const string Body = "SENTINEL-BODY";

    public Recorder Handler { get; }
    public SteppingClock Clock { get; } = new(DateTimeOffset.Parse("2026-11-16T10:00:00Z"));
    private int inFlight;
    public FakeLogger<PipelineEmailProvider> Logger { get; } = new();
    public FakeMapper Mapper { get; }
    public EmailSenderOptions Options { get; }
    public PipelineEmailProvider Provider { get; }
    public List<DateTimeOffset> CallTimes { get; } = [];

    public PipelineRig(
        ProviderCapabilities? capabilities = null,
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null,
        int timeoutSeconds = 30)
    {
        var count = 0;
        respond ??= (_, _, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"id":"msg-1"}"""));
        Handler = new Recorder(async (request, ct) =>
        {
            CallTimes.Add(Clock.GetUtcNow());
            Interlocked.Increment(ref inFlight);
            try
            {
                return await respond(++count, request, ct);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });
        Mapper = new FakeMapper(capabilities ?? new ProviderCapabilities());
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Mapper.ClientName).Returns(_ => new HttpClient(Handler, disposeHandler: false));
        Options = new EmailSenderOptions { TimeoutSeconds = timeoutSeconds };
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(Options);
        // Jitter fixed at the midpoint, so the back-off is exactly 1 s then 2 s.
        Provider = new PipelineEmailProvider(Mapper, factory, monitor, Clock, Logger) { Jitter = () => 0.5 };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) => Recorder.Json(status, body);

    public static HttpResponseMessage Status(int status, string body = "", string? retryAfter = null)
    {
        var response = Recorder.Json((HttpStatusCode)status, body);
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }

    public static OutboundEmail Email(
        int to = 1,
        int cc = 0,
        int bcc = 0,
        IReadOnlyList<OutboundAttachment>? attachments = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? replyTo = null)
        => new(
            $"Site <{Address}>",
            [.. Enumerable.Range(0, to).Select(i => i == 0 ? Address : $"to{i}@example.test")],
            [.. Enumerable.Range(0, cc).Select(i => $"cc{i}@example.test")],
            [.. Enumerable.Range(0, bcc).Select(i => $"bcc{i}@example.test")],
            replyTo ?? [],
            Subject,
            $"<p>{Body}</p>",
            null,
            attachments ?? [],
            headers ?? new Dictionary<string, string> { ["Expires"] = "Mon, 16 Nov 2026 11:00:00 GMT" });

    /// <summary>Runs a send, moving the fake clock only when the send is waiting on it.</summary>
    public Task<Exception?> SendAdvancingAsync(OutboundEmail? email = null)
        => Clock.DriveAsync(Provider.SendAsync(email ?? Email(), CancellationToken.None), () => Volatile.Read(ref inFlight));

    public double SecondsSinceFirstCall(int index) => (CallTimes[index] - CallTimes[0]).TotalSeconds;

    public static string HeaderValue(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? values.Single() : string.Empty;

    public static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;
}
