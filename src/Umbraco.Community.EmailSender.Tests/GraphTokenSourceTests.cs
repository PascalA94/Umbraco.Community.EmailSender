using System.Net;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.MicrosoftGraph;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Design 9.4, "Token cache": one token, shared by concurrent sends, refreshed early, never printed.</summary>
public class GraphTokenSourceTests
{
    internal const string Tenant = "11111111-2222-3333-4444-555555555555";
    internal const string ClientId = "535fb089-9ff3-47b6-9bfb-4f1264799865";
    internal const string Secret = "gs-SENTINEL&=+ %SECRET"; // gitleaks:allow
    internal const string Token = "gt-SENTINEL-TOKEN"; // gitleaks:allow

    internal static EmailSenderOptions Options(string? tenant = null) => new()
    {
        Provider = EmailProviderKind.MicrosoftGraph,
        MicrosoftGraph = { TenantId = tenant ?? Tenant, ClientId = ClientId, ClientSecret = Secret, SenderUserId = "sender@example.test" },
    };

    internal static string TokenJson(int expiresIn = 3599, string token = Token)
        => $$"""{"token_type":"Bearer","expires_in":{{expiresIn}},"ext_expires_in":{{expiresIn}},"access_token":"{{token}}"}""";

    private sealed class Rig : IDisposable
    {
        private readonly ServiceProvider services;

        public Rig(Func<int, Task<HttpResponseMessage>>? respond = null, EmailSenderOptions? options = null)
        {
            Server = new Recorder((_, _) => respond is null
                ? Task.FromResult(Recorder.Json(HttpStatusCode.OK, TokenJson()))
                : respond(Calls));
            Monitor = new TestOptionsMonitor(options ?? Options());
            var collection = new ServiceCollection();
            collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
            collection.AddSingleton<TimeProvider>(Clock);
            collection.AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<EmailSenderOptions>>(Monitor);
            collection.AddEmailProviderClient(GraphTokenSource.ClientName);
            collection.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Server));
            collection.AddSingleton<GraphTokenSource>();
            services = collection.BuildServiceProvider();
            Source = services.GetRequiredService<GraphTokenSource>();
            Logs = services.GetFakeLogCollector();
        }

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T10:00:00Z"));
        public Recorder Server { get; }
        public TestOptionsMonitor Monitor { get; }
        public GraphTokenSource Source { get; }
        public FakeLogCollector Logs { get; }
        public int Calls => Server.Calls.Count;

        public string LogText() => string.Join('\n', Logs.GetSnapshot().Select(r => $"{r.Message} {r.Exception} {string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])}"));

        public void Dispose() => services.Dispose();
    }

    [Fact]
    public async Task Request_IsFormWithClientCredentials()
    {
        using var rig = new Rig();

        Assert.Equal(Token, await rig.Source.GetAsync(CancellationToken.None));

        var (request, body) = Assert.Single(rig.Server.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token", request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
        Assert.Null(request.Headers.Authorization);
        Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "Umbraco.Community.EmailSender");

        var form = HttpUtility.ParseQueryString(body);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("https://graph.microsoft.com/.default", form["scope"]);
        Assert.Equal("client_credentials", form["grant_type"]);
        // The secret is form-encoded: its '&', '=', '+', ' ' and '%' must not split or merge fields.
        Assert.Equal(Secret, form["client_secret"]);
        Assert.DoesNotContain("&=+", body, StringComparison.Ordinal);

        // The field names are exactly those of the recorded example.
        var recorded = Conformance.FixtureLoader.Text("graph", "token-request.txt").Split("\n\n", 2)[1]
            .Replace("\r", string.Empty).Replace("\n", string.Empty).Split('&').Select(p => p.Split('=')[0]).Order();
        Assert.Equal(recorded, form.AllKeys.Order());
    }

    [Fact]
    public async Task Cached_UntilFiveMinutesBeforeExpiry()
    {
        using var rig = new Rig();
        await rig.Source.GetAsync(CancellationToken.None);

        // Lifetime 3599 s, refreshed once less than 300 s remain: still cached at 3298 s, gone by 3300 s.
        rig.Clock.Advance(TimeSpan.FromSeconds(3298));
        await rig.Source.GetAsync(CancellationToken.None);
        Assert.Equal(1, rig.Calls);

        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.Source.GetAsync(CancellationToken.None);
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task ShortLifetime_UsesSixtySecondMinimum()
    {
        using var rig = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.OK, TokenJson(expiresIn: 120))));
        await rig.Source.GetAsync(CancellationToken.None);

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        await rig.Source.GetAsync(CancellationToken.None);
        Assert.Equal(1, rig.Calls);

        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.Source.GetAsync(CancellationToken.None);
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task LifetimeShorterThanTheMinimum_IsNeverUsedPastExpiry()
    {
        using var rig = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.OK, TokenJson(expiresIn: 30))));
        await rig.Source.GetAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromSeconds(29));
        await rig.Source.GetAsync(CancellationToken.None);
        Assert.Equal(1, rig.Calls);

        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        await rig.Source.GetAsync(CancellationToken.None);

        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task ConcurrentSends_OneTokenRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rig = new Rig(async _ =>
        {
            await release.Task;
            return Recorder.Json(HttpStatusCode.OK, TokenJson());
        });

        var callers = Enumerable.Range(0, 20).Select(_ => Task.Run(async () => (await rig.Source.GetAsync(CancellationToken.None)).ToString())).ToArray();
        // Every caller has reached the source before the one request is answered.
        await WaitUntil(() => rig.Calls == 1);
        await Task.Delay(100);
        release.SetResult();
        var tokens = await Task.WhenAll(callers);

        Assert.All(tokens, t => Assert.Equal(Token, t));
        Assert.Equal(1, rig.Calls);
    }

    [Fact]
    public async Task WaitingCallerHonoursItsOwnCancellation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rig = new Rig(async _ =>
        {
            await release.Task;
            return Recorder.Json(HttpStatusCode.OK, TokenJson());
        });
        var first = Task.Run(async () => await rig.Source.GetAsync(CancellationToken.None));
        await WaitUntil(() => rig.Calls == 1);
        using var cts = new CancellationTokenSource();
        var second = rig.Source.GetAsync(cts.Token).AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.SetResult();
        Assert.Equal(Token, await first);
    }

    [Fact]
    public async Task Invalidate_ForcesNewToken()
    {
        using var rig = new Rig();
        await rig.Source.GetAsync(CancellationToken.None);

        rig.Source.Invalidate();
        await rig.Source.GetAsync(CancellationToken.None);

        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task OptionsChange_DropsToken()
    {
        using var rig = new Rig();
        await rig.Source.GetAsync(CancellationToken.None);
        var changed = Options();
        changed.MicrosoftGraph.ClientId = "99999999-9999-9999-9999-999999999999";

        rig.Monitor.Set(changed);
        await rig.Source.GetAsync(CancellationToken.None);

        Assert.Equal(2, rig.Calls);
        Assert.Equal("99999999-9999-9999-9999-999999999999", HttpUtility.ParseQueryString(rig.Server.Calls[1].Body)["client_id"]);
    }

    [Fact]
    public async Task OptionsChangeDuringARequest_DoesNotCacheTheStaleToken()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rig = new Rig(async calls =>
        {
            await release.Task;
            return Recorder.Json(HttpStatusCode.OK, TokenJson(token: calls == 1 ? "stale-token" : Token));
        });
        var first = Task.Run(async () => await rig.Source.GetAsync(CancellationToken.None));
        await WaitUntil(() => rig.Calls == 1);

        rig.Monitor.Set(Options());
        release.SetResult();
        await first;

        // The token fetched with the old settings is not kept for the new ones.
        Assert.Equal(Token, await rig.Source.GetAsync(CancellationToken.None));
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task TokenError_EchoesAllowListedCodeOnly()
    {
        var body = Conformance.FixtureLoader.Text("graph", "token-error.json");
        using var rig = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.BadRequest, body)));

        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));

        Assert.Equal(
            "Microsoft Graph token request failed: HTTP 400, invalid_scope, error code 70011. Check EmailSender:MicrosoftGraph:TenantId, ClientId and ClientSecret.",
            ex.Message);
        Assert.Null(ex.InnerException);
        foreach (var leak in new[] { "AADSTS", "Trace ID", "foo.microsoft.com", "0000aaaa", Tenant, ClientId })
        {
            Assert.DoesNotContain(leak, ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("""{"error":"has spaces SENTINEL-ECHO","error_codes":["SENTINEL-ECHO"]}""")]
    [InlineData("<html>SENTINEL-ECHO</html>")]
    [InlineData("")]
    public async Task TokenError_UnsafeOrMissingCode_IsStatusOnly(string body)
    {
        using var rig = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.BadGateway, body)));

        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));

        Assert.Equal("Microsoft Graph token request failed: HTTP 502. Check EmailSender:MicrosoftGraph:TenantId, ClientId and ClientSecret.", ex.Message);
    }

    [Theory]
    [InlineData("""{"token_type":"Bearer","expires_in":3599}""")]
    [InlineData("""{"access_token":"","expires_in":3599}""")]
    [InlineData("""{"access_token":"x","expires_in":0}""")]
    [InlineData("""{"access_token":"x"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"access_token":"a b","expires_in":3599}""")]
    [InlineData("""{"access_token":"a\r\nb","expires_in":3599}""")]
    public async Task UnreadableSuccess_Fails_AndIsNotCached(string body)
    {
        using var rig = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.OK, body)));

        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));
        await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));

        Assert.Equal("Microsoft Graph answered the token request but the token could not be read.", ex.Message);
        Assert.Equal(2, rig.Calls);
    }

    [Fact]
    public async Task ConnectionFailure_IsReportedByTypeName()
    {
        using var rig = new Rig(_ => throw new HttpRequestException($"SENTINEL-ECHO {Secret}"));

        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));

        Assert.Equal("Microsoft Graph token request failed: HttpRequestException.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Token_NeverInLogsOrExceptions()
    {
        using var rig = new Rig();
        await rig.Source.GetAsync(CancellationToken.None);
        using var failing = new Rig(_ => Task.FromResult(Recorder.Json(HttpStatusCode.Unauthorized, $$"""{"error":"invalid_client","error_description":"{{Token}} {{Secret}}"}""")));
        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await failing.Source.GetAsync(CancellationToken.None));

        var observed = string.Join('\n', rig.LogText(), failing.LogText(), ex.ToString(), rig.Monitor.CurrentValue.ToString(), rig.Monitor.CurrentValue.MicrosoftGraph.ToString(),
            GraphTokenResponse.TryParse(System.Text.Encoding.UTF8.GetBytes(TokenJson()))?.ToString());

        Assert.NotEmpty(rig.Logs.GetSnapshot());
        foreach (var sentinel in new[] { Token, Secret, "SENTINEL" })
        {
            Assert.DoesNotContain(sentinel, observed, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("x?y=1")]
    [InlineData("evil.test/../..")]
    [InlineData("a b")]
    [InlineData("tenant\n")]
    [InlineData("11111111-2222-3333-4444-555555555555\n")]
    [InlineData(" 11111111-2222-3333-4444-555555555555")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TenantId_Invalid_IsRefusedBeforeAnyRequest(string tenant)
    {
        using var rig = new Rig(options: Options(tenant));

        var ex = await Assert.ThrowsAsync<EmailSendException>(async () => await rig.Source.GetAsync(CancellationToken.None));

        Assert.Equal(MicrosoftGraphValidation.TenantMessage, ex.Message);
        Assert.Equal(0, rig.Calls);
    }

    [Theory]
    [InlineData(Tenant)]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("contoso.com")]
    public async Task TenantId_GuidOrDomain_BuildsTheDocumentedPath(string tenant)
    {
        using var rig = new Rig(options: Options(tenant));

        await rig.Source.GetAsync(CancellationToken.None);

        var uri = rig.Server.Calls[0].Request.RequestUri!;
        Assert.Equal("login.microsoftonline.com", uri.Host);
        Assert.Equal($"/{tenant}/oauth2/v2.0/token", uri.AbsolutePath);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(5);
        }
    }
}
