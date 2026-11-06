using System.Net;
using System.Security.Cryptography;
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
using Umbraco.Community.EmailSender.Providers.GovUkNotify;

namespace Umbraco.Community.EmailSender.Tests;

public class NotifyProviderTests
{
    private const string ServiceId = "11111111-2222-3333-4444-555555555555";
    private const string Secret = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ApiKey = "sentinel-key-" + ServiceId + "-" + Secret; // gitleaks:allow
    private const string TemplateId = "99999999-8888-7777-6666-555555555555";
    private const string ReplyToId = "12345678-1234-1234-1234-123456789012";

    private sealed class Rig
    {
        public Recorder Handler { get; }
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
        public FakeLogger<NotifyProvider> Logger { get; } = new();
        public NotifyProvider Provider { get; }

        public Rig(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null, EmailSenderOptions? options = null)
        {
            Handler = new Recorder(respond ?? ((_, _) => Task.FromResult(Json(HttpStatusCode.Created, """{"id":"740e5834-3a29-46b4-9a6f-16142fde533a"}"""))));
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(NotifyProvider.ClientName)
                .Returns(_ => new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri("https://api.notifications.service.gov.uk/") });
            var current = options ?? new EmailSenderOptions { Provider = EmailProviderKind.GovUkNotify };
            current.GovUkNotify.ApiKey ??= ApiKey;
            current.GovUkNotify.EmailTemplateId ??= TemplateId;
            var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
            monitor.CurrentValue.Returns(current);
            Provider = new NotifyProvider(factory, monitor, Clock, Logger);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => Recorder.Json(status, body);

    private static OutboundEmail Email(
        string subject = "Subject text",
        string? html = null,
        string? text = "Plain body",
        IReadOnlyList<string>? to = null,
        IReadOnlyList<string>? cc = null,
        IReadOnlyList<string>? bcc = null,
        IReadOnlyList<string>? replyTo = null,
        IReadOnlyList<OutboundAttachment>? attachments = null)
        => new(
            string.Empty,
            to ?? ["a@example.test"],
            cc ?? [],
            bcc ?? [],
            replyTo ?? [],
            subject,
            html,
            text,
            attachments ?? [],
            new Dictionary<string, string>());

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Send_PostsExpectedJsonWithSignedToken()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.GovUkNotify };
        options.GovUkNotify.ReplyToId = ReplyToId;
        var rig = new Rig(options: options);

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        var (request, body) = Assert.Single(rig.Handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v2/notifications/email", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);

        var parts = request.Headers.Authorization.Parameter!.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[0])));
        Assert.Equal("HS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        using var claims = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[1])));
        Assert.Equal(ServiceId, claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(rig.Clock.GetUtcNow().ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());
        var expected = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}")));
        Assert.Equal(expected, parts[2]);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("a@example.test", root.GetProperty("email_address").GetString());
        Assert.Equal(TemplateId, root.GetProperty("template_id").GetString());
        Assert.Equal(ReplyToId, root.GetProperty("email_reply_to_id").GetString());
        Assert.Equal("Subject text", root.GetProperty("personalisation").GetProperty("subject").GetString());
        Assert.Equal("Plain body", root.GetProperty("personalisation").GetProperty("body").GetString());
        Assert.False(root.TryGetProperty("reference", out _));
    }

    private static string Pad(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
    }

    [Fact]
    public async Task Send_WithoutReplyToId_OmitsTheField()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        Assert.DoesNotContain("email_reply_to_id", Assert.Single(rig.Handler.Calls).Body);
    }

    [Fact]
    public async Task Send_OneRequestPerRecipient()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(to: ["a@example.test", "b@example.test"]), CancellationToken.None);

        Assert.Equal(2, rig.Handler.Calls.Count);
        Assert.Contains("a@example.test", rig.Handler.Calls[0].Body);
        Assert.Contains("b@example.test", rig.Handler.Calls[1].Body);
    }

    [Fact]
    public async Task HtmlBody_IsConvertedToText_KeepingLinks()
    {
        var rig = new Rig();
        const string html = "<html><head><style>p{color:red}</style></head><body><p>Hello &amp; welcome</p><p>Set your password <a href=\"https://site.test/reset?token=abc&amp;x=1\">here</a>.</p><script>alert(1)</script><br>Bye</body></html>";

        await rig.Provider.SendAsync(Email(html: html, text: null), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var body = json.RootElement.GetProperty("personalisation").GetProperty("body").GetString()!;
        Assert.Contains("Hello & welcome", body);
        Assert.Contains("[here](https://site.test/reset?token=abc&x=1)", body);
        Assert.Contains("Bye", body);
        Assert.DoesNotContain("<", body);
        Assert.DoesNotContain("alert", body);
        Assert.DoesNotContain("color:red", body);
    }

    [Fact]
    public async Task HtmlBody_NonHttpLinksAreNotKept()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(html: "<a href=\"javascript:alert(1)\">x</a>", text: null), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var body = json.RootElement.GetProperty("personalisation").GetProperty("body").GetString()!;
        Assert.DoesNotContain("javascript", body);
    }

    [Fact]
    public async Task Attachments_FailClearlyBeforeSending()
    {
        var rig = new Rig();

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(
            Email(attachments: [new OutboundAttachment("a.pdf", "application/pdf", "QUJD")]), CancellationToken.None));

        Assert.Contains("attachments", ex.Message);
        Assert.Contains("GOV.UK Notify", ex.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CcAndBcc_FailClearlyBeforeSending(bool cc)
    {
        var rig = new Rig();

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(
            Email(cc: cc ? ["c@example.test"] : null, bcc: cc ? null : ["d@example.test"]), CancellationToken.None));

        Assert.Contains("Cc or Bcc", ex.Message);
        Assert.DoesNotContain("example.test", ex.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task TooManyRecipients_FailsBeforeSending()
    {
        var rig = new Rig();
        var many = Enumerable.Range(0, 51).Select(i => $"u{i}@example.test").ToList();

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(to: many), CancellationToken.None));

        Assert.Contains("50", ex.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task MessageReplyTo_IsIgnoredWithAWarningThatNamesNoAddress()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(replyTo: ["reply@example.test"]), CancellationToken.None);

        Assert.DoesNotContain("reply@example.test", Assert.Single(rig.Handler.Calls).Body);
        var warning = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("ReplyToId", warning.Message);
        Assert.DoesNotContain("example.test", warning.Message);
    }

    [Theory]
    [InlineData(400, """{"errors":[{"error":"BadRequestError","message":"email_address leak@example.test SENTINEL-BODY"}],"status_code":400}""", "HTTP 400, BadRequestError")]
    [InlineData(403, """{"errors":[{"error":"AuthError","message":"Invalid token: signature, api token not found SENTINEL-BODY"}],"status_code":403}""", "Check EmailSender:GovUkNotify:ApiKey")]
    [InlineData(429, """{"errors":[{"error":"RateLimitError","message":"SENTINEL-BODY"}],"status_code":429}""", "HTTP 429, RateLimitError")]
    [InlineData(500, "<html>SENTINEL-BODY</html>", "HTTP 500")]
    public async Task Errors_MapToMessageWithoutResponseText(int status, string body, string expected)
    {
        var rig = new Rig((_, _) => Task.FromResult(Json((HttpStatusCode)status, body)));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("SENTINEL-BODY", ex.Message);
        Assert.DoesNotContain("example.test", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task TeamOnlyKeyRefusal_GetsAHintWithoutEchoingTheResponse()
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"errors":[{"error":"BadRequestError","message":"Can't send to this recipient using a team-only API key leak@example.test"}],"status_code":400}""")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("team-only", ex.Message);
        Assert.DoesNotContain("leak@example.test", ex.Message);
    }

    [Fact]
    public async Task ErrorClassName_IsOnlyEchoedWhenItLooksLikeAClassName()
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"errors":[{"error":"leak@example.test","message":"x"}]}""")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.DoesNotContain("example.test", ex.Message);
    }

    [Fact]
    public async Task PartialFailure_SaysHowManyWereSent()
    {
        var n = 0;
        var rig = new Rig((_, _) => Task.FromResult(++n == 1
            ? Json(HttpStatusCode.Created, """{"id":"740e5834-3a29-46b4-9a6f-16142fde533a"}""")
            : Json(HttpStatusCode.TooManyRequests, """{"errors":[{"error":"RateLimitError","message":"x"}]}""")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(
            () => rig.Provider.SendAsync(Email(to: ["a@example.test", "b@example.test"]), CancellationToken.None));

        Assert.Contains("1 of 2", ex.Message);
    }

    [Fact]
    public async Task PartialFailure_ByNetworkException_StillSaysHowManyWereSent()
    {
        var n = 0;
        var rig = new Rig((_, _) => ++n == 1
            ? Task.FromResult(Json(HttpStatusCode.Created, """{"id":"740e5834-3a29-46b4-9a6f-16142fde533a"}"""))
            : throw new HttpRequestException("SENTINEL-BODY leak@example.test"));

        var ex = await Assert.ThrowsAsync<EmailSendException>(
            () => rig.Provider.SendAsync(Email(to: ["a@example.test", "b@example.test"]), CancellationToken.None));

        Assert.Contains("1 of 2", ex.Message);
        Assert.Contains("HttpRequestException", ex.Message);
        Assert.DoesNotContain("SENTINEL-BODY", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("<p>[Your account is locked](https://evil.example/login)</p>")]
    [InlineData("<p>&#91;Your account is locked&#93;(https://evil.example/login)</p>")]
    [InlineData("<p>&lbrack;Your account is locked&rbrack;(https://evil.example/login)</p>")]
    public async Task HtmlBody_LiteralMarkdownLink_IsNeutralised(string html)
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(html: html, text: null), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var body = json.RootElement.GetProperty("personalisation").GetProperty("body").GetString()!;
        Assert.DoesNotContain("[", body);
        Assert.DoesNotContain("](", body);
        Assert.Contains("Your account is locked", body);
    }

    [Fact]
    public async Task TextBody_LiteralMarkdownLink_IsNeutralised()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(text: "[Verify here](https://evil.example/login)"), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var body = json.RootElement.GetProperty("personalisation").GetProperty("body").GetString()!;
        Assert.DoesNotContain("](", body);
        Assert.DoesNotContain("[", body);
    }

    [Fact]
    public async Task HtmlBody_RealAnchorsStillBecomeLinks_WhileFreeTextBracketsAreNeutralised()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(html: "<p>See [note] and <a href=\"https://site.test/r\">reset</a></p>", text: null), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var body = json.RootElement.GetProperty("personalisation").GetProperty("body").GetString()!;
        Assert.Contains("[reset](https://site.test/r)", body);
        Assert.DoesNotContain("[note]", body);
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
            new EmailSenderOptions { Provider = EmailProviderKind.GovUkNotify, TimeoutSeconds = 5 });

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
        var rig = new Rig(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var cts = new CancellationTokenSource();

        var send = rig.Provider.SendAsync(Email(), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task UnexpectedException_IsWrappedByTypeNameOnly()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY leak@example.test"));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => rig.Provider.SendAsync(Email(), CancellationToken.None));

        Assert.Contains("HttpRequestException", ex.Message);
        Assert.DoesNotContain("SENTINEL-BODY", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Unicode_SubjectAndBody_RoundTrip()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(subject: "Café menu – ✓", text: "Crème brûlée 日本"), CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(rig.Handler.Calls).Body);
        var p = json.RootElement.GetProperty("personalisation");
        Assert.Equal("Café menu – ✓", p.GetProperty("subject").GetString());
        Assert.Equal("Crème brûlée 日本", p.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Send_LogsTheNotificationIdAtInformation()
    {
        var rig = new Rig();

        await rig.Provider.SendAsync(Email(), CancellationToken.None);

        Assert.Contains(rig.Logger.Collector.GetSnapshot(), r => r.Level == Microsoft.Extensions.Logging.LogLevel.Information && r.Message.Contains("740e5834-3a29-46b4-9a6f-16142fde533a"));
    }

    [Fact]
    public void NamedClient_HasNotifyBaseAddressAndNoRedirects()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotifyProvider();
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(NotifyProvider.ClientName);
        Assert.Equal(new Uri("https://api.notifications.service.gov.uk/"), client.BaseAddress);
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(NotifyProvider.ClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        Assert.False(handler is HttpClientHandler { AllowAutoRedirect: true } or SocketsHttpHandler { AllowAutoRedirect: true });
        Assert.True(handler is HttpClientHandler or SocketsHttpHandler);
    }

    [Fact]
    public void Provider_NeedsNoFromAddress() => Assert.False(((IEmailProvider)new Rig().Provider).UsesFromAddress);

    // Health check

    [Fact]
    public async Task Check_200WithPlaceholders_Success()
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            $$"""{"id":"{{TemplateId}}","type":"email","subject":"((subject))","body":"((body))"}""")));

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Success, status.ResultType);
        var (request, _) = Assert.Single(rig.Handler.Calls);
        Assert.Equal($"/v2/template/{TemplateId}", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Theory]
    [InlineData("""{"type":"email","subject":"Hello","body":"((body))"}""")]
    [InlineData("""{"type":"email","subject":"((subject))","body":"Hello"}""")]
    [InlineData("""{"type":"sms","subject":"((subject))","body":"((body))"}""")]
    [InlineData("""not json""")]
    public async Task Check_TemplateWithoutPlaceholders_Warning(string template)
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.OK, template)));

        var status = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Warning, status.ResultType);
        Assert.Contains("((subject))", status.Message);
        Assert.Contains("((body))", status.Message);
    }

    [Theory]
    [InlineData(403, StatusResultType.Error, "GOV.UK Notify rejected the API key.")]
    [InlineData(404, StatusResultType.Error, "no email template")]
    [InlineData(500, StatusResultType.Error, "HTTP 500")]
    public async Task Check_Failures_AreErrorsWithoutResponseText(int status, StatusResultType type, string expected)
    {
        var rig = new Rig((_, _) => Task.FromResult(Json((HttpStatusCode)status, """{"errors":[{"message":"SENTINEL-BODY"}]}""")));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(type, result.ResultType);
        Assert.Contains(expected, result.Message);
        Assert.DoesNotContain("SENTINEL-BODY", result.Message);
    }

    [Fact]
    public async Task Check_NetworkFailure_IsAnErrorNamingTheTypeOnly()
    {
        var rig = new Rig((_, _) => throw new HttpRequestException("SENTINEL-BODY"));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Error, result.ResultType);
        Assert.Contains("HttpRequestException", result.Message);
        Assert.DoesNotContain("SENTINEL-BODY", result.Message);
    }

    [Fact]
    public async Task Check_HugeTemplateBody_IsReadBounded()
    {
        var rig = new Rig((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"body\":\"" + new string('x', 5_000_000) + "\"}")));

        var result = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.Equal(StatusResultType.Warning, result.ResultType);
    }
}

public class NotifyApiKeyTests
{
    private const string Service = "11111111-2222-3333-4444-555555555555";
    private const string Secret = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Theory]
    [InlineData("name-" + Service + "-" + Secret)]
    [InlineData("a-b-c-" + Service + "-" + Secret)]
    [InlineData(Service + "-" + Secret)]
    public void Valid_ParsesServiceIdAndSecret(string key)
    {
        Assert.True(NotifyApiKey.TryParse(key, out var service, out var secret)); // gitleaks:allow
        Assert.Equal(Service, service);
        Assert.Equal(Secret, secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("name-" + Service + "_" + Secret)]
    [InlineData("name-" + Service + "-" + "zzzzzzzz-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [InlineData("name-" + Service + "-" + Secret + "\n")]
    [InlineData("name_" + Service + "-" + Secret)]
    public void Invalid_IsRejected(string key) => Assert.False(NotifyApiKey.TryParse(key, out _, out _));
}

public class NotifyOptionsTests
{
    private const string ApiKey = "name-11111111-2222-3333-4444-555555555555-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"; // gitleaks:allow
    private const string TemplateId = "99999999-8888-7777-6666-555555555555";

    private static EmailSenderOptions Valid()
    {
        var o = new EmailSenderOptions { Provider = EmailProviderKind.GovUkNotify };
        o.GovUkNotify.ApiKey = ApiKey;
        o.GovUkNotify.EmailTemplateId = TemplateId;
        return o;
    }

    private static ValidateOptionsResult Validate(EmailSenderOptions o) => new EmailSenderOptionsValidator().Validate(null, o);

    [Fact]
    public void Valid_Succeeds() => Assert.True(Validate(Valid()).Succeeded);

    [Fact]
    public void MissingApiKey_NamesKey()
    {
        var o = Valid();
        o.GovUkNotify.ApiKey = null;

        Assert.Contains("EmailSender:GovUkNotify:ApiKey", Validate(o).FailureMessage);
    }

    [Fact]
    public void MalformedApiKey_NamesKeyAndNeverEchoesIt()
    {
        var o = Valid();
        o.GovUkNotify.ApiKey = "SENTINEL-NOT-A-KEY";

        var message = Validate(o).FailureMessage;
        Assert.Contains("EmailSender:GovUkNotify:ApiKey", message);
        Assert.DoesNotContain("SENTINEL", message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void BadTemplateId_NamesKey(string? id)
    {
        var o = Valid();
        o.GovUkNotify.EmailTemplateId = id;

        Assert.Contains("EmailSender:GovUkNotify:EmailTemplateId", Validate(o).FailureMessage);
    }

    [Fact]
    public void BadReplyToId_NamesKey()
    {
        var o = Valid();
        o.GovUkNotify.ReplyToId = "nope";

        Assert.Contains("EmailSender:GovUkNotify:ReplyToId", Validate(o).FailureMessage);
    }

    [Fact]
    public void ToString_RedactsTheKey()
    {
        var o = Valid();

        Assert.DoesNotContain("aaaaaaaa", o.ToString());
        Assert.DoesNotContain("aaaaaaaa", o.GovUkNotify.ToString());
        Assert.DoesNotContain("aaaaaaaa", System.Text.Json.JsonSerializer.Serialize(o));
    }

    [Fact]
    public void Provider_ParsesCaseInsensitively()
    {
        Assert.True(EmailProviderKindParser.TryParse("govuknotify", out var kind));
        Assert.Equal(EmailProviderKind.GovUkNotify, kind);
    }
}
