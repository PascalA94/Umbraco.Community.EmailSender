using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Pipeline;

public class ProviderPipelineTests
{
    private static readonly Dictionary<string, string> TwoHeaders = new() { ["Expires"] = "Mon, 16 Nov 2026 11:00:00 GMT", ["X-Custom"] = "1" };

    [Theory]
    [InlineData(nameof(HeaderPolicy.SendAsGiven), "Expires,X-Custom")]
    [InlineData(nameof(HeaderPolicy.DropAll), "")]
    [InlineData(nameof(HeaderPolicy.XPrefixedOnly), "X-Custom")]
    public async Task HeaderPolicy_SendDropOrXOnly(string policy, string expected)
    {
        var rig = new PipelineRig(new ProviderCapabilities { Headers = Enum.Parse<HeaderPolicy>(policy) });

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(headers: TwoHeaders)));

        var sent = PipelineRig.Parse(Assert.Single(rig.Handler.Calls).Body).GetProperty("headers")
            .EnumerateObject().Select(p => p.Name);
        Assert.Equal(expected, string.Join(',', sent.Order()));
    }

    [Fact]
    public async Task IdempotencyKey_SameOnEveryAttempt_UniquePerSend()
    {
        var rig = new PipelineRig(
            new ProviderCapabilities { Idempotency = IdempotencyCarriage.Header },
            (n, _, _) => Task.FromResult(n % 2 == 1 ? PipelineRig.Status(503, retryAfter: "0") : PipelineRig.Json(HttpStatusCode.OK, "{}")));

        Assert.Null(await rig.SendAdvancingAsync());
        Assert.Null(await rig.SendAdvancingAsync());

        var keys = rig.Handler.Calls.Select(c => PipelineRig.HeaderValue(c.Request, "Idempotency-Key")).ToList();
        Assert.Equal(4, keys.Count);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal(keys[2], keys[3]);
        Assert.NotEqual(keys[0], keys[2]);
        Assert.True(Guid.TryParse(keys[0], out _));
        Assert.Equal([1, 2, 1, 2], rig.Mapper.Attempts.Select(a => a.Number));
    }

    [Fact]
    public async Task NoIdempotency_SendsNoKey()
    {
        var rig = new PipelineRig();

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.False(Assert.Single(rig.Handler.Calls).Request.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task EachAttempt_IsAFreshRequest()
    {
        var rig = new PipelineRig(respond: (n, _, _) => Task.FromResult(n == 1 ? PipelineRig.Status(503, retryAfter: "0") : PipelineRig.Json(HttpStatusCode.OK, "{}")));

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.NotSame(rig.Handler.Calls[0].Request, rig.Handler.Calls[1].Request);
        Assert.Equal(rig.Handler.Calls[0].Body, rig.Handler.Calls[1].Body);
    }

    [Fact]
    public async Task TwoXxFailClosed_UnreadableBody_Throws()
    {
        var rig = new PipelineRig(new ProviderCapabilities { FailClosedOn2xx = true }, (_, _, _) => Task.FromResult(PipelineRig.Json(HttpStatusCode.OK, "<html>")));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal("Fake answered HTTP 200 but the result could not be read.", ex.Message);
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task TwoXx_UnreadableBody_AcceptedWhenNotFailClosed()
    {
        var rig = new PipelineRig(respond: (_, _, _) => Task.FromResult(PipelineRig.Json(HttpStatusCode.Accepted, "")));

        Assert.Null(await rig.SendAdvancingAsync());

        Assert.Contains(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("message id none"));
    }

    [Fact]
    public async Task Rejected_TextUsesTheTemplate_AndDropsUnsafeCodes()
    {
        var rig = new PipelineRig(respond: (_, _, _) => Task.FromResult(PipelineRig.Status(422, """{"code":"bad person.SENTINEL@example.test"}""")));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal("Fake rejected the email: HTTP 422.", ex.Message);
    }

    [Fact]
    public async Task Rejected_WithCodeAndHint()
    {
        var outcome = ProviderOutcome.ForStatus(HttpStatusCode.Forbidden, "not_verified", "Verify the sender.");

        Assert.Equal("Fake rejected the email: HTTP 403, not_verified. Verify the sender.", ProviderPipeline.RejectionText("Fake", null, (ProviderOutcome.RejectedOutcome)outcome));
        Assert.Equal("Fake rejected the email: HTTP 403, ErrorCode not_verified. Verify the sender.", ProviderPipeline.RejectionText("Fake", "ErrorCode", (ProviderOutcome.RejectedOutcome)outcome));
    }

    [Fact]
    public void From_AddressAndNameParsed()
    {
        var named = PipelineRig.Email();
        var bare = named with { From = "only@example.test" };
        var none = named with { From = string.Empty };

        Assert.Equal(PipelineRig.Address, named.FromAddress);
        Assert.Equal("Site", named.FromName);
        Assert.Equal("only@example.test", bare.FromAddress);
        Assert.Null(bare.FromName);
        Assert.Equal(string.Empty, none.FromAddress);
        Assert.Null(none.FromName);
    }

    [Fact]
    public async Task Success_LogsCountsIdAttempts_NoPersonalData()
    {
        var rig = new PipelineRig();

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(to: 2, cc: 1, attachments: [new OutboundAttachment("SENTINEL-FILE.pdf", "application/pdf", "QUJD")])));

        var record = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Equal("Fake accepted the email: 3 recipients, 1 attachments, message id msg-1, 1 attempts, 0 ms", record.Message);
        AssertNoPersonalData(rig);
    }

    [Fact]
    public async Task Failure_LogsStatusAndCode_NoPersonalData()
    {
        var rig = new PipelineRig(respond: (_, _, _) => Task.FromResult(PipelineRig.Status(422, """{"code":"invalid_to"}""")));

        Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        var record = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Equal("Fake send failed: HTTP 422, code invalid_to", record.Message);
        AssertNoPersonalData(rig);
    }

    [Fact]
    public async Task ReplyTo_SingleStyle_WarnsWithTheCountDropped()
    {
        var rig = new PipelineRig(new ProviderCapabilities { ReplyTo = ReplyToStyle.Single });

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(replyTo: ["r1@example.test", "r2@example.test", "r3@example.test"])));

        var warning = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Equal("Fake takes one Reply-To address; 2 more were not sent", warning.Message);
    }

    [Fact]
    public async Task ReplyTo_NotSupported_WarnsWithTheHint()
    {
        var rig = new PipelineRig(new ProviderCapabilities { ReplyTo = ReplyToStyle.NotSupported, ReplyToHint = "Set Fake:ReplyToId." });

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(replyTo: ["r1@example.test"])));

        var warning = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Equal("Fake cannot use a per-message Reply-To, so it was ignored. Set Fake:ReplyToId.", warning.Message);
    }

    [Fact]
    public async Task PerRecipient_OneRequestEach_AndPartialFailureSaysHowMany()
    {
        var rig = new PipelineRig(
            new ProviderCapabilities { Recipients = RecipientPolicy.PerRecipient },
            (n, _, _) => Task.FromResult(n < 3 ? PipelineRig.Json(HttpStatusCode.OK, "{}") : PipelineRig.Status(400)));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync(PipelineRig.Email(to: 3)));

        Assert.Equal(3, rig.Handler.Calls.Count);
        Assert.All(rig.Handler.Calls, c => Assert.Single(PipelineRig.Parse(c.Body).GetProperty("to").EnumerateArray()));
        Assert.Equal("Fake rejected the email: HTTP 400. Sent to 2 of 3 recipients before this failure.", ex.Message);
    }

    [Fact]
    public async Task UnexpectedException_WrappedByTypeNameOnly()
    {
        var rig = new PipelineRig(respond: (_, _, _) => throw new FormatException("SENTINEL-BODY person.SENTINEL@example.test"));

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal("Fake send failed: FormatException.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Health_NoSafeCheck_IsInfo()
    {
        var rig = new PipelineRig();

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Equal("Fake is configured. Credentials are not checked without sending.", status.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public void Client_NoRedirect_NoLogging_UserAgentAndAccept()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddEmailProviderClient("community.emailSender.fake");
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("community.emailSender.fake");
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Null(client.BaseAddress);
        Assert.Equal("application/json", Assert.Single(client.DefaultRequestHeaders.Accept).MediaType);
        var agent = Assert.Single(client.DefaultRequestHeaders.UserAgent);
        Assert.Equal("Umbraco.Community.EmailSender", agent.Product!.Name);
        Assert.False(string.IsNullOrEmpty(agent.Product.Version));
        Assert.DoesNotContain("+", agent.Product.Version);

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("community.emailSender.fake");
        var chain = new List<HttpMessageHandler>();
        while (handler is not null)
        {
            chain.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        Assert.DoesNotContain(chain, h => h.GetType().Name.Contains("Logging", StringComparison.Ordinal));
        var primary = Assert.IsType<SocketsHttpHandler>(chain[^1]);
        Assert.False(primary.AllowAutoRedirect);
    }

    private static void AssertNoPersonalData(PipelineRig rig)
    {
        foreach (var record in rig.Logger.Collector.GetSnapshot())
        {
            var state = string.Join("|", record.StructuredState?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);
            foreach (var text in new[] { record.Message, state })
            {
                Assert.DoesNotContain("SENTINEL", text);
                Assert.DoesNotContain("example.test", text);
            }
        }
    }

    private sealed class HostRetryHandler : DelegatingHandler;

    /// <summary>
    /// A site-wide <c>ConfigureHttpClientDefaults</c> (Aspire ServiceDefaults' resilience handler, a Polly policy) would wrap
    /// every provider client and resend POSTs the pipeline deliberately does not retry (M6 security review). Every registered
    /// provider's client must end up with none of the host's handlers, no factory logging and no redirects, whichever order the
    /// host registers its defaults in.
    /// </summary>
    [Theory]
    [MemberData(nameof(KindsAndOrder))]
    public void HostDefaultHandlers_AreCleared_NoRedirect_NoLogging_ForEveryProvider(Umbraco.Community.EmailSender.Configuration.EmailProviderKind kind, bool defaultsFirst)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptionsMonitor<Umbraco.Community.EmailSender.Configuration.EmailSenderOptions>>());
        void Defaults() => services.ConfigureHttpClientDefaults(b => b.AddHttpMessageHandler(() => new HostRetryHandler()));
        if (defaultsFirst)
        {
            Defaults();
        }

        Umbraco.Community.EmailSender.Providers.Registry.ProviderRegistry.Get(kind).Register(services);
        if (!defaultsFirst)
        {
            Defaults();
        }

        using var provider = services.BuildServiceProvider();
        var clientName = provider.GetService<IProviderMapper>()?.ClientName
            ?? Umbraco.Community.EmailSender.Providers.AzureCommunicationServices.AcsProvider.ClientName;

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        var chain = new List<HttpMessageHandler>();
        while (handler is not null)
        {
            chain.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        Assert.DoesNotContain(chain, h => h is HostRetryHandler);
        Assert.DoesNotContain(chain, h => h.GetType().Name.Contains("Logging", StringComparison.Ordinal));
        Assert.False(Assert.IsType<SocketsHttpHandler>(chain[^1]).AllowAutoRedirect);
    }

    public static TheoryData<Umbraco.Community.EmailSender.Configuration.EmailProviderKind, bool> KindsAndOrder()
    {
        var data = new TheoryData<Umbraco.Community.EmailSender.Configuration.EmailProviderKind, bool>();
        foreach (var definition in Umbraco.Community.EmailSender.Providers.Registry.ProviderRegistry.All)
        {
            data.Add(definition.Kind, true);
            data.Add(definition.Kind, false);
        }

        return data;
    }
}
