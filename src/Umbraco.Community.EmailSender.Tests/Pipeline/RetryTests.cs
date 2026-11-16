using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Pipeline;

/// <summary>
/// Design 9.2 retry rules: retry only when the provider certainly did not accept the email, or when an idempotency key makes
/// a repeat safe. A duplicate invite is worse than a failed one, and a long wait hangs the backoffice request thread.
/// </summary>
public class RetryTests
{
    private static readonly ProviderCapabilities Idempotent = new() { Idempotency = IdempotencyCarriage.Header };

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Sequence(params Func<HttpResponseMessage>[] answers)
        => (n, _, _) => Task.FromResult(answers[Math.Min(n, answers.Length) - 1]());

    private static HttpResponseMessage Ok() => PipelineRig.Json(HttpStatusCode.OK, """{"id":"msg-1"}""");

    [Fact]
    public async Task Retry429_HonoursRetryAfterSeconds()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429, retryAfter: "3"), Ok));

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.Equal(2, rig.Handler.Calls.Count);
        Assert.InRange(rig.SecondsSinceFirstCall(1), 3.0, 3.11);
        Assert.Contains(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("2 attempts"));
    }

    [Fact]
    public async Task Retry429_HonoursRetryAfterHttpDate()
    {
        PipelineRig? rigRef = null;
        var rig = new PipelineRig(respond: (n, _, _) => Task.FromResult(n == 1
            ? PipelineRig.Status(429, retryAfter: rigRef!.Clock.GetUtcNow().AddSeconds(4).ToString("R", CultureInfo.InvariantCulture))
            : Ok()));
        rigRef = rig;

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.Equal(2, rig.Handler.Calls.Count);
        Assert.InRange(rig.SecondsSinceFirstCall(1), 3.9, 4.11);
    }

    [Fact]
    public async Task Retry429_NoHeader_BacksOff1Then2()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429), () => PipelineRig.Status(429), Ok));

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.Equal(3, rig.Handler.Calls.Count);
        Assert.InRange(rig.SecondsSinceFirstCall(1), 1.0, 1.11);
        Assert.InRange(rig.SecondsSinceFirstCall(2), 3.0, 3.11);
    }

    [Theory]
    [InlineData(0.0, 0.8)]
    [InlineData(1.0, 1.2)]
    public void Backoff_JitterIsPlusOrMinus20Percent(double random, double expectedSeconds)
        => Assert.Equal(expectedSeconds, ProviderPipeline.Backoff(1, () => random).TotalSeconds, 3);

    [Theory]
    [InlineData(420)]
    [InlineData(503)]
    public async Task OtherNotAcceptedSignals_AreRetried(int status)
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(status, retryAfter: "1"), Ok));

        Assert.Null(await rig.SendAdvancingAsync());
        Assert.Equal(2, rig.Handler.Calls.Count);
    }

    [Fact]
    public async Task Wait_Over10s_FailsAtOnce()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429, retryAfter: "60"), Ok));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Single(rig.Handler.Calls);
        Assert.Equal("Fake is rate limiting this site (HTTP 429). Try again later.", ex.Message);
        Assert.Equal(TimeSpan.Zero, rig.Clock.GetUtcNow() - rig.CallTimes[0]);
    }

    [Fact]
    public async Task Wait_PastBudget_FailsAtOnce()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429, retryAfter: "8"), Ok), timeoutSeconds: 5);

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Single(rig.Handler.Calls);
        Assert.Equal("Fake is rate limiting this site (HTTP 429). Try again later.", ex.Message);
    }

    [Fact]
    public async Task QuotaStop_NotRetried()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429, """{"code":"quota"}""", retryAfter: "1"), Ok));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Single(rig.Handler.Calls);
        Assert.Equal("Fake rejected the email: HTTP 429, quota.", ex.Message);
    }

    [Theory]
    [InlineData(500, false, 1)]
    [InlineData(502, false, 1)]
    [InlineData(504, false, 1)]
    [InlineData(500, true, 2)]
    [InlineData(502, true, 2)]
    [InlineData(504, true, 2)]
    public async Task Status500_RetriedOnlyWithIdempotency(int status, bool idempotent, int expectedCalls)
    {
        var rig = new PipelineRig(idempotent ? Idempotent : null, Sequence(() => PipelineRig.Status(status), Ok));

        var error = await rig.SendAdvancingAsync();

        Assert.Equal(expectedCalls, rig.Handler.Calls.Count);
        Assert.Equal(idempotent, error is null);
        if (idempotent)
        {
            Assert.Equal(
                PipelineRig.HeaderValue(rig.Handler.Calls[0].Request, "Idempotency-Key"),
                PipelineRig.HeaderValue(rig.Handler.Calls[1].Request, "Idempotency-Key"));
        }
        else
        {
            Assert.Equal($"Fake rejected the email: HTTP {status}.", error!.Message);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task ConnectionFailure_RetriedOnlyWithIdempotency(bool idempotent, int expectedCalls)
    {
        var rig = new PipelineRig(idempotent ? Idempotent : null, (n, _, _) =>
            n == 1 ? throw new HttpRequestException("connection refused to SENTINEL-HOST") : Task.FromResult(Ok()));

        var error = await rig.SendAdvancingAsync();

        Assert.Equal(expectedCalls, rig.Handler.Calls.Count);
        if (!idempotent)
        {
            var ex = Assert.IsType<EmailSendException>(error);
            Assert.Equal("Fake send failed: HttpRequestException.", ex.Message);
            Assert.Null(ex.InnerException);
        }
        else
        {
            Assert.Null(error);
        }
    }

    [Fact]
    public async Task MaxThreeAttempts()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(503, retryAfter: "0")));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal(3, rig.Handler.Calls.Count);
        Assert.Equal("Fake rejected the email: HTTP 503.", ex.Message);
    }

    [Fact]
    public async Task MaxThreeAttempts_429_EndsWithRateLimitText()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(429, retryAfter: "0")));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal(3, rig.Handler.Calls.Count);
        Assert.Equal("Fake is rate limiting this site (HTTP 429). Try again later.", ex.Message);
    }

    [Fact]
    public async Task Timeout_IsBudgetAcrossAttempts()
    {
        var rig = new PipelineRig(timeoutSeconds: 3, respond: async (n, _, ct) =>
        {
            if (n == 1)
            {
                return PipelineRig.Status(503, retryAfter: "1");
            }

            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal(2, rig.Handler.Calls.Count);
        Assert.Equal("Fake did not answer within 3 seconds.", ex.Message);
        Assert.InRange((rig.Clock.GetUtcNow() - rig.CallTimes[0]).TotalSeconds, 3.0, 3.11);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(422)]
    public async Task Other4xx_NotRetried(int status)
    {
        var rig = new PipelineRig(Idempotent, Sequence(() => PipelineRig.Status(status), Ok));

        Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task CallerCancellation_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        var rig = new PipelineRig(respond: async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        var task = rig.Provider.SendAsync(PipelineRig.Email(), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    // The retry table is the pipeline's, not the mapper's: a mapper that marks a possibly accepted send retryable must not
    // cause a resend (M6 security review).
    [Theory]
    [InlineData(500)]
    [InlineData(400)]
    [InlineData(200)]
    public async Task MapperCannotWidenTheRetryTable(int status)
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(status == 200 ? 500 : status), Ok));
        rig.Mapper.Override = r => r.StatusCode == HttpStatusCode.OK ? null : ProviderOutcome.Rejected(status, null, null, retryable: true, quotaStop: false);

        Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task MapperCanNarrowTheRetryTable()
    {
        var rig = new PipelineRig(respond: Sequence(() => PipelineRig.Status(503, retryAfter: "0"), Ok));
        rig.Mapper.Override = r => r.StatusCode == HttpStatusCode.OK ? null : ProviderOutcome.Rejected(503, null, null, retryable: false, quotaStop: false);

        Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());
        Assert.Single(rig.Handler.Calls);
    }
}
