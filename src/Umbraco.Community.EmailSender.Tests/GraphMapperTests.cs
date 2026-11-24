using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.MicrosoftGraph;
using Umbraco.Community.EmailSender.Providers.Pipeline;
using Umbraco.Community.EmailSender.Providers.Registry;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Microsoft Graph quirks the shared conformance suite cannot express (design 9.4); the suite covers the rest.</summary>
public class GraphMapperTests
{
    private const string FirstToken = "first-SENTINEL-TOKEN"; // gitleaks:allow
    private const string SecondToken = "second-SENTINEL-TOKEN"; // gitleaks:allow

    private sealed class Rig : IDisposable
    {
        private readonly ServiceProvider services;
        private int tokenCalls;

        public Rig(Func<HttpRequestMessage, Task<HttpResponseMessage>>? send = null, Func<HttpRequestMessage, Task<HttpResponseMessage>>? token = null, Action<EmailSenderOptions>? tweak = null)
        {
            Options = GraphTokenSourceTests.Options();
            tweak?.Invoke(Options);
            Server = new Recorder((request, _) => request.RequestUri!.Host == "login.microsoftonline.com"
                ? (token ?? DefaultToken)(request)
                : (send ?? (_ => Task.FromResult(Recorder.Json(HttpStatusCode.Accepted, string.Empty))))(request));
            var collection = new ServiceCollection();
            collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
            collection.AddSingleton<TimeProvider>(Clock);
            collection.AddSingleton<IOptionsMonitor<EmailSenderOptions>>(new TestOptionsMonitor(Options));
            ProviderRegistry.Get(EmailProviderKind.MicrosoftGraph).Register(collection);
            collection.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Server));
            services = collection.BuildServiceProvider();
            Provider = services.GetRequiredService<IEmailProvider>();
            Logs = services.GetFakeLogCollector();
        }

        public EmailSenderOptions Options { get; }
        public SteppingClock Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T10:00:00Z"));
        public Recorder Server { get; }
        public IEmailProvider Provider { get; }
        public FakeLogCollector Logs { get; }

        public List<(HttpRequestMessage Request, string Body)> Sends => [.. Server.Calls.Where(c => c.Request.RequestUri!.Host == "graph.microsoft.com")];
        public List<(HttpRequestMessage Request, string Body)> Tokens => [.. Server.Calls.Where(c => c.Request.RequestUri!.Host == "login.microsoftonline.com")];

        // A new token for each request, so a refresh is visible in the Authorization header of the send that follows.
        private Task<HttpResponseMessage> DefaultToken(HttpRequestMessage request)
            => Task.FromResult(Recorder.Json(HttpStatusCode.OK, GraphTokenSourceTests.TokenJson(token: Interlocked.Increment(ref tokenCalls) == 1 ? FirstToken : SecondToken)));

        public Task<Exception?> SendAsync(OutboundEmail email) => Clock.DriveAsync(Provider.SendAsync(email, CancellationToken.None), () => 0);

        public JsonElement Body(int index = 0) => JsonDocument.Parse(Sends[index].Body).RootElement.Clone();

        public void Dispose() => services.Dispose();
    }

    private static OutboundEmail Email() => Conformance.Scenarios.Minimal();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Accepts202And204(HttpStatusCode status)
    {
        using var rig = new Rig(send: _ => Task.FromResult(Recorder.Json(status, string.Empty)));

        Assert.Null(await rig.SendAsync(Email()));

        Assert.Single(rig.Sends);
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("message id none,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Request_GoesToTheMailboxWithTheBearerToken()
    {
        using var rig = new Rig();

        Assert.Null(await rig.SendAsync(Email()));

        var (request, _) = Assert.Single(rig.Sends);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/v1.0/users/{rig.Options.MicrosoftGraph.SenderUserId}/sendMail", Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(FirstToken, request.Headers.Authorization.Parameter);
        Assert.Single(rig.Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveToSentItems_FollowsTheOption(bool save)
    {
        using var rig = new Rig(tweak: o => o.MicrosoftGraph.SaveToSentItems = save);

        Assert.Null(await rig.SendAsync(Email()));

        Assert.Equal(save, rig.Body().GetProperty("saveToSentItems").GetBoolean());
    }

    [Fact]
    public async Task Body_IsHtmlOrText_NeverBoth()
    {
        using var rig = new Rig();

        await rig.SendAsync(Email() with { HtmlBody = "<p>hello</p>", TextBody = null });
        await rig.SendAsync(Email());

        var html = rig.Body(0).GetProperty("message").GetProperty("body");
        var text = rig.Body(1).GetProperty("message").GetProperty("body");
        Assert.Equal(("HTML", "<p>hello</p>"), (html.GetProperty("contentType").GetString(), html.GetProperty("content").GetString()));
        Assert.Equal(("Text", Conformance.Scenarios.Body), (text.GetProperty("contentType").GetString(), text.GetProperty("content").GetString()));
    }

    [Fact]
    public async Task Attachment_IsAFileAttachment_NeverInline()
    {
        using var rig = new Rig();

        await rig.SendAsync(Email() with { Attachments = [new OutboundAttachment("report.pdf", "application/pdf", "QUJDRA==")] });

        var attachment = rig.Body().GetProperty("message").GetProperty("attachments")[0];
        Assert.Equal("#microsoft.graph.fileAttachment", attachment.GetProperty("@odata.type").GetString());
        Assert.False(attachment.TryGetProperty("isInline", out var inline) && inline.GetBoolean());
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnceThenFails()
    {
        using var rig = new Rig(send: _ => Task.FromResult(Recorder.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"InvalidAuthenticationToken","message":"SENTINEL-ECHO"}}""")));

        var error = await rig.SendAsync(Email());

        Assert.Equal(
            "Microsoft Graph rejected the email: HTTP 401, InvalidAuthenticationToken. The token was rejected. Check the app's Mail.Send permission and admin consent.",
            error?.Message);
        // One send, one fresh token, one repeat: a second 401 is final.
        Assert.Equal(2, rig.Sends.Count);
        Assert.Equal(2, rig.Tokens.Count);
        Assert.Equal([FirstToken, SecondToken], rig.Sends.Select(s => s.Request.Headers.Authorization!.Parameter));
    }

    [Fact]
    public async Task Unauthorized_ThenAccepted_SendsOnceWithTheFreshToken()
    {
        var sends = 0;
        using var rig = new Rig(send: _ => Task.FromResult(Interlocked.Increment(ref sends) == 1
            ? Recorder.Json(HttpStatusCode.Unauthorized, string.Empty)
            : Recorder.Json(HttpStatusCode.Accepted, string.Empty)));

        Assert.Null(await rig.SendAsync(Email()));

        Assert.Equal([FirstToken, SecondToken], rig.Sends.Select(s => s.Request.Headers.Authorization!.Parameter));
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("2 attempts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Forbidden_IsNotRefreshed()
    {
        using var rig = new Rig(send: _ => Task.FromResult(Recorder.Json(HttpStatusCode.Forbidden, string.Empty)));

        var error = await rig.SendAsync(Email());

        Assert.Contains("HTTP 403", error?.Message);
        Assert.Single(rig.Sends);
        Assert.Single(rig.Tokens);
    }

    [Fact]
    public async Task TokenFailure_FailsTheSend_WithoutASendRequest()
    {
        using var rig = new Rig(token: _ => Task.FromResult(Recorder.Json(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_codes":[7000215],"error_description":"SENTINEL-ECHO"}""")));

        var error = await rig.SendAsync(Email());

        Assert.Equal(
            "Microsoft Graph token request failed: HTTP 400, invalid_client, error code 7000215. Check EmailSender:MicrosoftGraph:TenantId, ClientId and ClientSecret.",
            error?.Message);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    public async Task Headers_XPrefixOnly_ExpiresDropped()
    {
        using var rig = new Rig();
        var headers = new Dictionary<string, string> { ["Expires"] = Conformance.Scenarios.Expires, ["X-Group-Id"] = "NV001", ["x-other"] = "two" };

        Assert.Null(await rig.SendAsync(Email() with { Headers = headers }));

        var sent = rig.Body().GetProperty("message").GetProperty("internetMessageHeaders").EnumerateArray().Select(h => h.GetProperty("name").GetString()).Order();
        Assert.Equal(["X-Group-Id", "x-other"], sent);
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Debug && r.Message.Contains("1 not sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoHeaders_NoInternetMessageHeadersProperty()
    {
        using var rig = new Rig();

        await rig.SendAsync(Email() with { Headers = new Dictionary<string, string> { ["Expires"] = Conformance.Scenarios.Expires } });

        Assert.False(rig.Body().GetProperty("message").TryGetProperty("internetMessageHeaders", out _));
    }

    [Fact]
    public async Task MessageFrom_IgnoredWithDebug()
    {
        using var rig = new Rig();

        Assert.Null(await rig.SendAsync(Email() with { From = "Site Name <site.SENTINEL@example.test>" }));

        var message = rig.Body().GetProperty("message");
        Assert.False(message.TryGetProperty("from", out _));
        Assert.False(message.TryGetProperty("sender", out _));
        var lines = rig.Logs.GetSnapshot().Where(r => r.Message.Contains("message From is not used", StringComparison.Ordinal)).ToList();
        Assert.Equal(LogLevel.Debug, Assert.Single(lines).Level);
        Assert.DoesNotContain("SENTINEL", lines[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMessageFrom_NoDebugLine()
    {
        using var rig = new Rig();

        await rig.SendAsync(Email() with { From = string.Empty });

        Assert.DoesNotContain(rig.Logs.GetSnapshot(), r => r.Message.Contains("message From is not used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachmentOver3Mb_RefusedBeforeSend()
    {
        using var rig = new Rig();
        var big = Convert.ToBase64String(new byte[(3 * 1024 * 1024) + 1]);
        var ok = Convert.ToBase64String(new byte[3 * 1024 * 1024]);

        var refused = await rig.SendAsync(Email() with { Attachments = [new OutboundAttachment("big.bin", "application/octet-stream", big)] });
        var accepted = await rig.SendAsync(Email() with { Attachments = [new OutboundAttachment("ok.bin", "application/octet-stream", ok)] });

        Assert.Equal("Microsoft Graph accepts attachments up to 3 MB each.", refused?.Message);
        Assert.Null(accepted);
        // The refusal came before any request: the one token request and the one send belong to the accepted email.
        Assert.Single(rig.Sends);
        Assert.Single(rig.Tokens);
    }

    [Fact]
    public async Task RequestOver35Mb_RefusedBeforeSend()
    {
        // Twelve attachments of 3 MB are 48 MB after base64: each passes the per-attachment rule, the request does not pass the cap.
        using var rig = new Rig(tweak: o => o.MaxTotalAttachmentBytes = 50 * 1024 * 1024);
        var each = Convert.ToBase64String(new byte[3 * 1024 * 1024]);

        var error = await rig.SendAsync(Email() with { Attachments = [.. Enumerable.Range(0, 12).Select(i => new OutboundAttachment($"f{i}.bin", "application/octet-stream", each))] });

        Assert.Equal("The email is larger than Microsoft Graph accepts in one request (35 MB after encoding). Remove or shrink the attachments.", error?.Message);
        Assert.Empty(rig.Sends);
    }

    [Theory]
    [InlineData("user@contoso.com", "/v1.0/users/user@contoso.com/sendMail")]
    [InlineData("22222222-3333-4444-5555-666666666666", "/v1.0/users/22222222-3333-4444-5555-666666666666/sendMail")]
    public async Task SenderUserId_GuidOrAddress_BuildsTheDocumentedPath(string sender, string path)
    {
        using var rig = new Rig(tweak: o => o.MicrosoftGraph.SenderUserId = sender);

        Assert.Null(await rig.SendAsync(Email()));

        var uri = Assert.Single(rig.Sends).Request.RequestUri!;
        Assert.Equal("graph.microsoft.com", uri.Host);
        Assert.Equal(path, Uri.UnescapeDataString(uri.AbsolutePath));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a%2F..%2Fb@contoso.com")]
    [InlineData("user@contoso.com/../../x")]
    [InlineData("user@contoso.com?x=1")]
    [InlineData("Name <user@contoso.com>")]
    [InlineData("a@b.test, c@d.test")]
    [InlineData("22222222-3333-4444-5555-666666666666\n")]
    [InlineData(" 22222222-3333-4444-5555-666666666666")]
    [InlineData("")]
    public async Task SenderUserId_Invalid_IsRefusedBeforeAnyRequest(string sender)
    {
        using var rig = new Rig(tweak: o => o.MicrosoftGraph.SenderUserId = sender);

        var error = await rig.SendAsync(Email());

        Assert.Equal(MicrosoftGraphValidation.SenderMessage, error?.Message);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    public async Task ErrorCode_IsTheAllowListedErrorCodeOnly()
    {
        using var rig = new Rig(send: _ => Task.FromResult(Recorder.Json(HttpStatusCode.NotFound,
            """{"error":{"code":"ErrorInvalidUser","message":"person.SENTINEL@example.test is gone","innerError":{"code":"inner SENTINEL","request-id":"abc"}}}""")));

        var error = await rig.SendAsync(Email());

        Assert.Equal("Microsoft Graph rejected the email: HTTP 404, ErrorInvalidUser. Check EmailSender:MicrosoftGraph:SenderUserId: the mailbox must exist and be licensed.", error?.Message);
    }

    [Fact]
    public async Task ServerError_IsNeverRetried_BecauseThereIsNoIdempotencyKey()
    {
        using var rig = new Rig(send: _ => Task.FromResult(Recorder.Json(HttpStatusCode.BadGateway, string.Empty)));

        var error = await rig.SendAsync(Email());

        Assert.Equal("Microsoft Graph rejected the email: HTTP 502.", error?.Message);
        Assert.Single(rig.Sends);
    }

    // A 503 may follow an accepted send and Graph has no idempotency key, so it is the stricter rule: only 429 retries (design 9.4).
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ServerErrors_IncludingA503_AreNeverRetried(HttpStatusCode status)
    {
        using var rig = new Rig(send: _ =>
        {
            var response = Recorder.Json(status, string.Empty);
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });

        var error = await rig.SendAsync(Email());

        Assert.Equal($"Microsoft Graph rejected the email: HTTP {(int)status}.", error?.Message);
        Assert.Single(rig.Sends);
    }

    [Fact]
    public async Task Throttled429_IsStillRetried()
    {
        var sends = 0;
        using var rig = new Rig(send: _ =>
        {
            var response = Interlocked.Increment(ref sends) == 1 ? Recorder.Json(HttpStatusCode.TooManyRequests, string.Empty) : Recorder.Json(HttpStatusCode.Accepted, string.Empty);
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });

        Assert.Null(await rig.SendAsync(Email()));

        Assert.Equal(2, rig.Sends.Count);
    }

    // The refresh is an attempt like any other: it never makes a fourth.
    [Fact]
    public async Task Unauthorized_OnTheLastAttempt_IsNotRefreshed()
    {
        var sends = 0;
        using var rig = new Rig(send: _ =>
        {
            var response = Interlocked.Increment(ref sends) < 3 ? Recorder.Json(HttpStatusCode.TooManyRequests, string.Empty) : Recorder.Json(HttpStatusCode.Unauthorized, string.Empty);
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });

        var error = await rig.SendAsync(Email());

        Assert.Contains("HTTP 401", error?.Message);
        Assert.Equal(3, rig.Sends.Count);
    }

    [Fact]
    public async Task HealthCheck_TokenOnly_Text()
    {
        using var rig = new Rig();

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        Assert.Equal("Microsoft Graph issued a token. Mail.Send and the mailbox scope are not proven without sending.", status.Message);
        Assert.Single(rig.Tokens);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    public async Task HealthCheck_TokenRefused_IsAnError_WithTheTokenFailureText()
    {
        using var rig = new Rig(token: _ => Task.FromResult(Recorder.Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_codes":[7000215]}""")));

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.Contains("HTTP 401, invalid_client", status.Message);
        Assert.Empty(rig.Sends);
    }

    [Fact]
    public async Task HealthCheck_ReusesTheCachedToken()
    {
        using var rig = new Rig();

        await rig.Provider.CheckAsync(CancellationToken.None);
        await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Single(rig.Tokens);
    }

    [Fact]
    public void Provider_UsesNoFromAddress_AndNamesItselfPlainly()
    {
        using var rig = new Rig();

        Assert.False(rig.Provider.UsesFromAddress);
        Assert.Equal("Microsoft Graph", rig.Provider.Name);
    }

    [Fact]
    public void Capabilities_MatchDesign94()
    {
        using var rig = new Rig();
        var caps = ((PipelineEmailProvider)rig.Provider).Mapper.Capabilities;

        Assert.Equal(["graph.microsoft.com"], caps.AllowedHosts);
        Assert.Equal(3L * 1024 * 1024, caps.MaxAttachmentBytesEach);
        Assert.Equal(35L * 1024 * 1024, caps.MaxRequestBytes);
        Assert.Equal(HeaderPolicy.XPrefixedOnly, caps.Headers);
        Assert.Equal(IdempotencyCarriage.None, caps.Idempotency);
        Assert.Equal(TrackingStyle.NotApplicable, caps.Tracking);
    }

    // The mailbox and tenant are validated at start-up; messages name the key, never the value.
    [Fact]
    public void Validation_MissingEverything_NamesEveryKey()
    {
        var result = new EmailSenderOptionsValidator().Validate(null, new EmailSenderOptions { Provider = EmailProviderKind.MicrosoftGraph });

        Assert.True(result.Failed);
        foreach (var key in new[] { "ClientSecret", "TenantId", "ClientId", "SenderUserId" })
        {
            Assert.Contains($"EmailSender:MicrosoftGraph:{key} is required", result.FailureMessage);
        }
    }

    [Fact]
    public void Validation_ValidOptions_Pass()
        => Assert.True(new EmailSenderOptionsValidator().Validate(null, GraphTokenSourceTests.Options()).Succeeded);

    [Theory]
    [InlineData("tenant-SENTINEL/../x", "11111111-2222-3333-4444-555555555555", "a@b.test", "TenantId")]
    [InlineData("contoso.com", "{535fb089-9ff3-47b6-9bfb-4f1264799865}", "a@b.test", "ClientId")]
    [InlineData("contoso.com", "client-SENTINEL", "a@b.test", "ClientId")]
    [InlineData("contoso.com", "535fb089-9ff3-47b6-9bfb-4f1264799865", "Name SENTINEL <a@b.test>", "SenderUserId")]
    [InlineData("contoso.com", "535fb089-9ff3-47b6-9bfb-4f1264799865", "sender-SENTINEL", "SenderUserId")]
    public void Validation_BadValue_NamesTheKeyNeverTheValue(string tenant, string client, string sender, string key)
    {
        var options = GraphTokenSourceTests.Options(tenant);
        options.MicrosoftGraph.ClientId = client;
        options.MicrosoftGraph.SenderUserId = sender;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"EmailSender:MicrosoftGraph:{key}", result.FailureMessage);
        Assert.DoesNotContain("SENTINEL", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(GraphTokenSourceTests.Secret, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_SecretWithTrailingNewline_IsRefused()
    {
        var options = GraphTokenSourceTests.Options();
        options.MicrosoftGraph.ClientSecret = "secret\n";

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.Contains("EmailSender:MicrosoftGraph:ClientSecret contains a control or non-ASCII character", result.FailureMessage);
    }

    [Fact]
    public void Options_ToStringAndJson_HideTheSecret()
    {
        var options = GraphTokenSourceTests.Options();

        Assert.DoesNotContain(GraphTokenSourceTests.Secret, options.MicrosoftGraph.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(GraphTokenSourceTests.Secret, JsonSerializer.Serialize(options), StringComparison.Ordinal);
    }
}
