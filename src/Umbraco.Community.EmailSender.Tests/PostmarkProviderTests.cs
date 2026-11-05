using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Postmark;

namespace Umbraco.Community.EmailSender.Tests;

public class PostmarkProviderTests
{
    private const string Token = "pm-SENTINEL-TOKEN"; // gitleaks:allow

    private sealed class Rig
    {
        public Recorder Handler { get; }
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
        public FakeLogger<PostmarkProvider> Logger { get; } = new();
        public PostmarkProvider Provider { get; }

        public Rig(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null, EmailSenderOptions? options = null)
        {
            Handler = new Recorder(respond ?? ((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"ErrorCode":0,"Message":"OK","MessageID":"0a129aee-e1cd-480d-b08d-4f48548ff48d"}"""))));
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(PostmarkProvider.ClientName)
                .Returns(_ => new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri("https://api.postmarkapp.com/") });
            var current = options ?? new EmailSenderOptions { Provider = EmailProviderKind.Postmark };
            current.Postmark.ServerToken ??= Token;
            var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
            monitor.CurrentValue.Returns(current);
            Provider = new PostmarkProvider(factory, monitor, Clock, Logger);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => Recorder.Json(status, body);

    private static OutboundEmail Email(
        string subject = "Subject text",
        string? html = "<p>hi</p>",
        string? text = null,
        IReadOnlyList<OutboundAttachment>? attachments = null,
        IReadOnlyList<string>? to = null,
        IReadOnlyList<string>? cc = null,
        IReadOnlyList<string>? bcc = null)
        => new(
            "Site <from@example.test>",
            to ?? ["a@example.test", "b@example.test"],
            cc ?? ["c@example.test"],
            bcc ?? ["d@example.test"],
            ["reply@example.test"],
            subject,
            html,
            text,
            attachments ?? [],
            new Dictionary<string, string> { ["Expires"] = "Wed, 04 Nov 2026 11:00:00 GMT" });

    [Fact]
    public async Task Send_PostsExpectedJson()
    {
        var rig = new Rig();
        var email = Email(attachments: [new OutboundAttachment("a.pdf", "application/pdf", "QUJD")]);

        await rig.Provider.SendAsync(email, CancellationToken.None);

        var (request, body) = Assert.Single(rig.Handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/email", request.RequestUri!.AbsolutePath);
        Assert.Equal(Token, Assert.Single(request.Headers.GetValues("X-Postmark-Server-Token")));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("Site <from@example.test>", root.GetProperty("From").GetString());
        Assert.Equal("a@example.test,b@example.test", root.GetProperty("To").GetString());
        Assert.Equal("c@example.test", root.GetProperty("Cc").GetString());
        Assert.Equal("d@example.test", root.GetProperty("Bcc").GetString());
        Assert.Equal("reply@example.test", root.GetProperty("ReplyTo").GetString());
        Assert.Equal("Subject text", root.GetProperty("Subject").GetString());
        Assert.Equal("<p>hi</p>", root.GetProperty("HtmlBody").GetString());
        Assert.False(root.TryGetProperty("TextBody", out _));
        Assert.Equal("outbound", root.GetProperty("MessageStream").GetString());
        var header = Assert.Single(root.GetProperty("Headers").EnumerateArray());
        Assert.Equal("Expires", header.GetProperty("Name").GetString());
        Assert.Equal("Wed, 04 Nov 2026 11:00:00 GMT", header.GetProperty("Value").GetString());
        var attachment = Assert.Single(root.GetProperty("Attachments").EnumerateArray());
        Assert.Equal("a.pdf", attachment.GetProperty("Name").GetString());
        Assert.Equal("QUJD", attachment.GetProperty("Content").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("ContentType").GetString());
    }

    [Fact]
    public async Task Send_TextOnly_SendsTextBodyAndNoRecipientFieldsWhenEmpty()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(html: null, text: "plain", cc: [], bcc: []), CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        Assert.Equal("plain", json.RootElement.GetProperty("TextBody").GetString());
        Assert.False(json.RootElement.TryGetProperty("HtmlBody", out _));
        Assert.False(json.RootElement.TryGetProperty("Cc", out _));
        Assert.False(json.RootElement.TryGetProperty("Bcc", out _));
    }

    [Fact]
    public async Task Send_UsesConfiguredMessageStream()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark, Postmark = { MessageStream = "broadcast" } };
        var rig = new Rig(options: options);

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        Assert.Equal("broadcast", json.RootElement.GetProperty("MessageStream").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisableLinkTracking_SetsTrackLinksNone(bool disable)
    {
        var rig = new Rig(options: new EmailSenderOptions { Provider = EmailProviderKind.Postmark, DisableLinkTracking = disable });

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        if (disable)
        {
            Assert.Equal("None", json.RootElement.GetProperty("TrackLinks").GetString());
            Assert.False(json.RootElement.GetProperty("TrackOpens").GetBoolean());
        }
        else
        {
            Assert.False(json.RootElement.TryGetProperty("TrackLinks", out _));
            Assert.False(json.RootElement.TryGetProperty("TrackOpens", out _));
        }
    }

    [Theory]
    [InlineData(401, """{"ErrorCode":10,"Message":"Bad or missing Server token SENTINEL-BODY"}""", "HTTP 401, ErrorCode 10")]
    [InlineData(422, """{"ErrorCode":300,"Message":"Invalid email request SENTINEL-BODY"}""", "HTTP 422, ErrorCode 300")]
    [InlineData(500, "<html>SENTINEL-BODY</html>", "HTTP 500")]
    [InlineData(422, """{"ErrorCode":"300 SENTINEL-BODY"}""", "HTTP 422")]
    public async Task Errors_MapToMessage(int status, string body, string expected)
    {
        var rig = new Rig((_, _) => Task.FromResult(Json((HttpStatusCode)status, body)));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("Postmark", ex.Message);
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Errors_ReadAtMostFourKilobytesOfBody()
    {
        var padding = new string(' ', 5000);
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, padding + """{"ErrorCode":300}""")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.DoesNotContain("ErrorCode 300", ex.Message);
    }

    [Fact]
    public async Task Timeout_ThrowsEmailSendException()
    {
        var entered = new TaskCompletionSource();
        var rig = new Rig(
            async (_, ct) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Json(HttpStatusCode.OK, "{}");
            },
            new EmailSenderOptions { Provider = EmailProviderKind.Postmark, TimeoutSeconds = 5 });

        var send = rig.Provider.SendAsync(Email(), CancellationToken.None);
        await entered.Task;
        rig.Clock.Advance(TimeSpan.FromSeconds(6));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => send);
        Assert.Contains("5 seconds", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var rig = new Rig(async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });

        var send = rig.Provider.SendAsync(Email(), cts.Token);
        await entered.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task Unicode_SubjectAndFileName_RoundTrip()
    {
        var rig = new Rig();
        var email = Email(subject: "Café – menu ☕", attachments: [new OutboundAttachment("Café menu.pdf", "application/pdf", "QUJD")]);

        await rig.Provider.SendAsync(email, CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        Assert.Equal("Café – menu ☕", json.RootElement.GetProperty("Subject").GetString());
        Assert.Equal("Café menu.pdf", json.RootElement.GetProperty("Attachments")[0].GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Redirect_IsAFailureAndTheTokenIsNotResent()
    {
        var rig = new Rig((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example.test/email");
            return Task.FromResult(redirect);
        });

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("HTTP 302", ex.Message);
        Assert.Single(rig.Handler.Calls);
        Assert.DoesNotContain("evil.example.test", ex.Message);
    }

    [Fact]
    public void NamedClient_HasPostmarkBaseAddressAndNoRedirects()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostmarkProvider();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PostmarkProvider.ClientName);
        Assert.Equal(new Uri("https://api.postmarkapp.com/"), client.BaseAddress);
        Assert.False(client.DefaultRequestHeaders.Contains("X-Postmark-Server-Token"));

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PostmarkProvider.ClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        Assert.False(handler is HttpClientHandler { AllowAutoRedirect: true } or SocketsHttpHandler { AllowAutoRedirect: true });
        Assert.True(handler is HttpClientHandler or SocketsHttpHandler);
    }

    [Fact]
    public async Task MoreThanFiftyRecipients_FailsBeforeSending()
    {
        var rig = new Rig();
        var many = Enumerable.Range(0, 51).Select(i => $"u{i}@example.test").ToList();

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(to: many, cc: [], bcc: []), CancellationToken.None));

        Assert.Contains("50", ex.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task Send_LogsMessageIdAtInformation()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        var record = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Message.Contains("0a129aee"));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, record.Level);
    }

    [Fact]
    public async Task Check_200_Success()
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"Name":"Server"}""")));

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        var (request, _) = Assert.Single(rig.Handler.Calls);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/server", request.RequestUri!.AbsolutePath);
        Assert.Equal(Token, Assert.Single(request.Headers.GetValues("X-Postmark-Server-Token")));
    }

    [Theory]
    [InlineData(401, "Postmark rejected the server token.")]
    [InlineData(500, "Postmark answered HTTP 500.")]
    public async Task Check_Failure_ErrorText(int status, string expected)
    {
        var rig = new Rig((_, _) => Task.FromResult(Json((HttpStatusCode)status, """{"Message":"SENTINEL-BODY"}""")));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, result.ResultType);
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task Check_Unreachable_ErrorWithoutExceptionText()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY"));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, result.ResultType);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Fact]
    public async Task UnexpectedException_IsWrappedByTypeNameOnly()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY leak@example.test"));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("HttpRequestException", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task InvalidTokenHeaderValue_IsWrappedAndNeverEchoed()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark, Postmark = { ServerToken = "pm-SENTINEL\r\nX-Evil: 1" } }; // gitleaks:allow
        var rig = new Rig(options: options);

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public void Header_ToString_DoesNotPrintValues()
        => Assert.DoesNotContain("SENTINEL", new PostmarkHeader("SENTINEL", "SENTINEL").ToString());

    [Fact]
    public void NamedClient_RedactsAllLoggedHeaders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostmarkProvider();
        using var provider = services.BuildServiceProvider();

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>().Get(PostmarkProvider.ClientName);

        Assert.True(factoryOptions.ShouldRedactHeaderValue("X-Postmark-Server-Token"));
    }
}
