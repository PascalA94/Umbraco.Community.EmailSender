using Microsoft.Extensions.Http;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;

namespace Umbraco.Community.EmailSender.Tests;

public class AcsProviderTests
{
    /// <summary>A non-seekable body, like a real socket: the SDK's seekable-stream path is not what production sees.</summary>
    private sealed class NetworkLikeStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A syntactically valid connection string with a made-up key.
    private static readonly string ConnectionString =
        $"endpoint=https://sentinel-resource.communication.azure.com/;accesskey={Convert.ToBase64String("SENTINEL-ACCESS-KEY"u8)}"; // gitleaks:allow

    private static HttpResponseMessage Accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StreamContent(new NetworkLikeStream("""{"id":"operation-id-1","status":"Running"}"""u8.ToArray())),
        };
        response.Content.Headers.ContentType = new("application/json");
        response.Headers.Add("Operation-Location", "https://sentinel-resource.communication.azure.com/emails/operations/operation-id-1?api-version=2025-09-01");
        return response;
    }

    private sealed class Rig
    {
        public Recorder Handler { get; }
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
        public FakeLogger<AcsProvider> Logger { get; } = new();
        public AcsProvider Provider { get; }

        public Rig(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null, EmailSenderOptions? options = null, string? connectionString = null)
        {
            Handler = new Recorder(respond ?? ((_, _) => Task.FromResult(Accepted())));
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(AcsProvider.ClientName).Returns(_ => new HttpClient(Handler, disposeHandler: false));
            var current = options ?? new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices };
            current.AzureCommunicationServices.ConnectionString = connectionString ?? ConnectionString;
            var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
            monitor.CurrentValue.Returns(current);
            Provider = new AcsProvider(factory, monitor, Clock, Logger);
        }
    }

    private static OutboundEmail Email(
        string subject = "Subject text",
        string? html = "<p>hi</p>",
        string? text = null,
        IReadOnlyList<OutboundAttachment>? attachments = null)
        => new(
            "Site <from@example.test>",
            ["a@example.test", "b@example.test"],
            ["c@example.test"],
            ["d@example.test"],
            ["reply@example.test"],
            subject,
            html,
            text,
            attachments ?? [],
            new Dictionary<string, string> { ["Expires"] = "Wed, 04 Nov 2026 11:00:00 GMT" });

    [Fact]
    public async Task Send_BuildsEmailMessage()
    {
        var rig = new Rig();
        var email = Email(attachments: [new OutboundAttachment("a.pdf", "application/pdf", "QUJD")]);

        await rig.Provider.SendAsync(email, CancellationToken.None);

        var (request, body) = Assert.Single(rig.Handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/emails:send", request.RequestUri!.AbsolutePath);
        Assert.Equal("sentinel-resource.communication.azure.com", request.RequestUri.Host);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("from@example.test", root.GetProperty("senderAddress").GetString());
        var recipients = root.GetProperty("recipients");
        Assert.Equal(["a@example.test", "b@example.test"], recipients.GetProperty("to").EnumerateArray().Select(e => e.GetProperty("address").GetString()));
        Assert.Equal("c@example.test", recipients.GetProperty("cc")[0].GetProperty("address").GetString());
        Assert.Equal("d@example.test", recipients.GetProperty("bcc")[0].GetProperty("address").GetString());
        Assert.Equal("reply@example.test", root.GetProperty("replyTo")[0].GetProperty("address").GetString());
        var content = root.GetProperty("content");
        Assert.Equal("Subject text", content.GetProperty("subject").GetString());
        Assert.Equal("<p>hi</p>", content.GetProperty("html").GetString());
        Assert.False(content.TryGetProperty("plainText", out _));
        Assert.Equal("Wed, 04 Nov 2026 11:00:00 GMT", root.GetProperty("headers").GetProperty("Expires").GetString());
        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
        Assert.Equal("a.pdf", attachment.GetProperty("name").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        Assert.Equal("QUJD", attachment.GetProperty("contentInBase64").GetString());
        Assert.True(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task Send_TextOnly_SendsPlainText()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(html: null, text: "plain"), CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        Assert.Equal("plain", json.RootElement.GetProperty("content").GetProperty("plainText").GetString());
        Assert.False(json.RootElement.GetProperty("content").TryGetProperty("html", out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisableLinkTracking_SetsUserEngagementTrackingDisabled(bool disable)
    {
        var rig = new Rig(options: new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices, DisableLinkTracking = disable });

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        var present = json.RootElement.TryGetProperty("userEngagementTrackingDisabled", out var value);
        Assert.Equal(disable, present && value.GetBoolean());
    }

    [Fact]
    public async Task Send_UsesWaitUntilStarted()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        // WaitUntil.Completed would poll the operation URL with GETs until the email is delivered.
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task Send_LogsOperationIdAtInformation()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        var record = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Message.Contains("operation-id-1"));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, record.Level);
    }

    [Theory]
    [InlineData(403, "HTTP 403")]
    [InlineData(429, "HTTP 429")]
    public async Task RequestFailed_MapsToEmailSendException(int status, string expected)
    {
        var rig = new Rig((_, _) => Task.FromResult(Recorder.Json((HttpStatusCode)status,
            """{"error":{"code":"AccessDenied","message":"SENTINEL-BODY leak@example.test"}}""")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("Azure Communication Services", ex.Message);
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.DoesNotContain("leak@example.test", ex.Message);
        Assert.Null(ex.InnerException);
        // The SDK's default retry would resend a POST that may already have been accepted.
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task RequestFailed_ErrorCodeWithOddCharacters_IsLeftOut()
    {
        var rig = new Rig((_, _) =>
        {
            var response = Recorder.Json(HttpStatusCode.BadRequest, "{}");
            response.Headers.Add("x-ms-error-code", "SENTINEL <b>oops</b>");
            return Task.FromResult(response);
        });

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.DoesNotContain("SENTINEL", ex.Message);
    }

    [Fact]
    public async Task InvalidConnectionString_FailsNamingTheKeyNotTheValue()
    {
        var rig = new Rig(connectionString: "endpoint=not-a-url;accesskey=SENTINEL-KEY");

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("EmailSender:AzureCommunicationServices:ConnectionString", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task Timeout_ThrowsEmailSendException()
    {
        var entered = new TaskCompletionSource();
        var rig = new Rig(
            async (_, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return Accepted();
            },
            new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices, TimeoutSeconds = 5 });

        var send = rig.Provider.SendAsync(Email(), CancellationToken.None);
        await entered.Task;
        rig.Clock.Advance(TimeSpan.FromSeconds(6));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => send);
        Assert.Contains("5 seconds", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Unicode_SubjectAndFileName_RoundTrip()
    {
        var rig = new Rig();
        var email = Email(subject: "Café – menu ☕", attachments: [new OutboundAttachment("Café menu.pdf", "application/pdf", "QUJD")]);

        await rig.Provider.SendAsync(email, CancellationToken.None);

        using var json = JsonDocument.Parse(rig.Handler.Calls.Single().Body);
        Assert.Equal("Café – menu ☕", json.RootElement.GetProperty("content").GetProperty("subject").GetString());
        Assert.Equal("Café menu.pdf", json.RootElement.GetProperty("attachments")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ChangedConnectionString_IsPickedUp()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices };
        var rig = new Rig(options: options);
        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        options.AzureCommunicationServices.ConnectionString =
            $"endpoint=https://other-resource.communication.azure.com/;accesskey={Convert.ToBase64String("OTHER"u8)}"; // gitleaks:allow
        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        Assert.Equal("other-resource.communication.azure.com", rig.Handler.Calls[1].Request.RequestUri!.Host);
    }

    [Fact]
    public void NamedClient_FollowsNoRedirects()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcsProvider();
        using var provider = services.BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(AcsProvider.ClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        Assert.True(handler is SocketsHttpHandler { AllowAutoRedirect: false });
    }

    [Fact]
    public async Task Check_BadConnectionString_Error()
    {
        var rig = new Rig(connectionString: "SENTINEL-not-a-connection-string");

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, result.ResultType);
        Assert.Contains("EmailSender:AzureCommunicationServices:ConnectionString", result.Message);
        Assert.DoesNotContain("SENTINEL", result.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task Check_EndpointAnswers_SuccessAndSendsNoKey()
    {
        var rig = new Rig((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Success, result.ResultType);
        Assert.Contains("cannot", result.Message); // says plainly that sending itself is not proven
        var (request, _) = Assert.Single(rig.Handler.Calls);
        Assert.Equal("sentinel-resource.communication.azure.com", request.RequestUri!.Host);
        Assert.False(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task Check_Unreachable_ErrorWithoutExceptionText()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY"));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, result.ResultType);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Theory]
    [InlineData("endpoint=http://sentinel-resource.communication.azure.com/;accesskey=U0VOVElORUw=")] // not https
    [InlineData("endpoint=https://evil.example.test/;accesskey=U0VOVElORUw=")] // not an ACS host
    [InlineData("endpoint=https://10.0.0.1/;accesskey=U0VOVElORUw=")] // IP literal
    [InlineData("endpoint=http://evil.example.test/;ENDPOINT=https://sentinel-resource.communication.azure.com/;accesskey=U0VOVElORUw=")] // duplicate, mixed case
    [InlineData("endpoint=http://evil.example.test/; endpoint=https://sentinel-resource.communication.azure.com/;accesskey=U0VOVElORUw=")] // duplicate, whitespace
    [InlineData("endpoint=https://sentinel-resource.communication.azure.com/;accesskey=U0VOVElORUw=;SENTINEL=1")] // unknown segment
    [InlineData("endpoint=https://sentinel-resource.communication.azure.com/;accesskey=U0VOVElORUw=;SENTINEL")] // no keyword
    [InlineData("endpoint=https://sentinel-resource.communication.azure.com/;accesskey=not base64 SENTINEL")] // key is not base64
    public async Task ConnectionString_OnlyOneStrictInterpretationIsAccepted(string connectionString) // gitleaks:allow
    {
        var rig = new Rig(connectionString: connectionString);

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("EmailSender:AzureCommunicationServices:ConnectionString", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Empty(rig.Handler.Calls);
        var check = await rig.Provider.CheckAsync(CancellationToken.None);
        Assert.Equal(StatusResultType.Error, check.ResultType);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task TransportFailure_IsReportedAsUnreachableNotHttpZero()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY leak@example.test"));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("could not be reached", ex.Message);
        Assert.DoesNotContain("HTTP 0", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task UnexpectedException_IsWrappedByTypeNameOnly()
    {
        var rig = new Rig();
        var email = Email(attachments: [new OutboundAttachment("a.pdf", "application/pdf", "***SENTINEL not base64")]);

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(email, CancellationToken.None));

        Assert.Contains("FormatException", ex.Message);
        Assert.DoesNotContain("SENTINEL", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_StaysACancellation()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        var rig = new Rig(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Accepted();
        });

        var send = rig.Provider.SendAsync(Email(), cts.Token);
        await entered.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public void NamedClient_RedactsAllLoggedHeaders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAcsProvider();
        using var provider = services.BuildServiceProvider();

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(AcsProvider.ClientName);

        Assert.True(factoryOptions.ShouldRedactHeaderValue("Authorization"));
        Assert.True(factoryOptions.ShouldRedactHeaderValue("x-ms-content-sha256"));
    }
}
