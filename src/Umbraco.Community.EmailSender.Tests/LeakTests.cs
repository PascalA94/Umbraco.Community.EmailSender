using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.GovUkNotify;
using Umbraco.Community.EmailSender.Providers.Postmark;


namespace Umbraco.Community.EmailSender.Tests;

/// <summary>
/// Every failure path of every provider, with sentinel secrets, an address and a subject that the fake server also echoes
/// back, must leave none of them in a log record, an exception, a health-check text or a <c>ToString()</c>.
/// </summary>
public class LeakTests
{
    private const string PostmarkToken = "pm-SENTINEL-TOKEN"; // gitleaks:allow
    private const string AcsKeyRaw = "SENTINEL-ACCESS-KEY"; // gitleaks:allow
    private const string NotifySecret = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"; // gitleaks:allow
    private const string NotifyService = "11111111-2222-3333-4444-555555555555";
    private const string Address = "leak@example.test";
    private const string Subject = "SENTINEL-SUBJECT";
    private const string BodyText = "SENTINEL-BODY-TEXT";
    private const string FileName = "SENTINEL-FILE.pdf";

    private static readonly string AcsConnectionString =
        $"endpoint=https://sentinel-resource.communication.azure.com/;accesskey={Convert.ToBase64String(Encoding.UTF8.GetBytes(AcsKeyRaw))}"; // gitleaks:allow

    private static readonly string[] Sentinels =
    [
        PostmarkToken, AcsKeyRaw, Convert.ToBase64String(Encoding.UTF8.GetBytes(AcsKeyRaw)), NotifySecret, NotifyService,
        "sentinel-key", "eyJ", "Bearer ", "HMAC-SHA256", "Signature=", Address, "example.test", Subject, BodyText, FileName, "SENTINEL-ECHO",
    ];

    public enum Failure
    {
        Unauthorized,
        Forbidden,
        BadRequest,
        TooManyRequests,
        ServerError,
        NetworkException,
        MalformedBody,
        AttachmentTooLarge,
        NoFrom,
    }

    public static TheoryData<EmailProviderKind, Failure> Paths()
    {
        var data = new TheoryData<EmailProviderKind, Failure>();
        foreach (var kind in Enum.GetValues<EmailProviderKind>())
        {
            foreach (var failure in Enum.GetValues<Failure>())
            {
                data.Add(kind, failure);
            }
        }

        return data;
    }

    private static EmailSenderOptions Options(EmailProviderKind kind)
    {
        var options = new EmailSenderOptions { Provider = kind, MaxTotalAttachmentBytes = 4 };
        options.Postmark.ServerToken = PostmarkToken;
        options.AzureCommunicationServices.ConnectionString = AcsConnectionString;
        options.GovUkNotify.ApiKey = $"sentinel-key-{NotifyService}-{NotifySecret}";
        options.GovUkNotify.EmailTemplateId = "99999999-8888-7777-6666-555555555555";
        return options;
    }

    private static HttpResponseMessage Echo(HttpRequestMessage request, HttpStatusCode status, bool malformed)
    {
        // The server repeats everything it was sent, as a hostile or careless API might.
        var headers = string.Join(' ', request.Headers.Select(h => $"{h.Key}={string.Join(',', h.Value)}"));
        var body = malformed
            ? $"<<<{Address} {Subject} {PostmarkToken} SENTINEL-ECHO {headers}"
            : $$"""{"ErrorCode":300,"Message":"{{Address}} {{Subject}} {{PostmarkToken}} SENTINEL-ECHO {{headers}}","errors":[{"error":"BadRequestError","message":"{{Address}} {{Subject}} SENTINEL-ECHO {{headers}}"}]}""";
        return Recorder.Json(status, body);
    }

    private static Recorder Server(Failure failure) => new((request, _) => failure switch
    {
        Failure.NetworkException => throw new HttpRequestException($"{Address} {Subject} {PostmarkToken} SENTINEL-ECHO"),
        Failure.Unauthorized => Task.FromResult(Echo(request, HttpStatusCode.Unauthorized, false)),
        Failure.Forbidden => Task.FromResult(Echo(request, HttpStatusCode.Forbidden, false)),
        Failure.BadRequest => Task.FromResult(Echo(request, (HttpStatusCode)422, false)),
        Failure.TooManyRequests => Task.FromResult(Echo(request, HttpStatusCode.TooManyRequests, false)),
        Failure.MalformedBody => Task.FromResult(Echo(request, HttpStatusCode.InternalServerError, true)),
        _ => Task.FromResult(Echo(request, HttpStatusCode.InternalServerError, false)),
    });

    private static (IEmailProvider Provider, Func<string> ProviderLog) Build(EmailProviderKind kind, EmailSenderOptions options, Recorder server)
    {
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(options);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(server, disposeHandler: false)
        {
            BaseAddress = kind switch
            {
                EmailProviderKind.Postmark => new Uri("https://api.postmarkapp.com/"),
                EmailProviderKind.GovUkNotify => new Uri("https://api.notifications.service.gov.uk/"),
                _ => null,
            },
        });

        switch (kind)
        {
            case EmailProviderKind.Postmark:
                var pm = new FakeLogger<PostmarkProvider>();
                return (new PostmarkProvider(factory, monitor, clock, pm), () => Dump(pm));
            case EmailProviderKind.GovUkNotify:
                var notify = new FakeLogger<NotifyProvider>();
                return (new NotifyProvider(factory, monitor, clock, notify), () => Dump(notify));
            case EmailProviderKind.AzureCommunicationServices:
                var acs = new FakeLogger<AcsProvider>();
                return (new AcsProvider(factory, monitor, clock, acs), () => Dump(acs));
            default:
                // A new provider must be added here on purpose, never tested as ACS by accident.
                throw new NotSupportedException($"Add {kind} to LeakTests.Build.");
        }
    }

    private static EmailMessage Message(Failure failure)
    {
        var attachments = failure == Failure.AttachmentTooLarge
            ? new List<EmailMessageAttachment> { new(new MemoryStream(new byte[64]), FileName) }
            : null;
        return new EmailMessage(
            failure == Failure.NoFrom ? string.Empty : "from@example.test",
            [Address],
            null!,
            null!,
            null!,
            Subject,
            $"<p>{BodyText}</p>",
            true,
            attachments);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task NoSecretsOrRecipients_OnAnyFailurePath(EmailProviderKind kind, Failure failure)
    {
        var options = Options(kind);
        var (provider, providerLog) = Build(kind, options, Server(failure));
        var clientLogger = new FakeLogger<HttpApiEmailSenderClient>();
        var globalSettings = Substitute.For<IOptionsMonitor<GlobalSettings>>();
        globalSettings.CurrentValue.Returns(new GlobalSettings());
        var optionsMonitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        optionsMonitor.CurrentValue.Returns(options);
        var client = new HttpApiEmailSenderClient(optionsMonitor, globalSettings, provider, new FakeTimeProvider(), clientLogger);

        var observed = new StringBuilder();
        // Every path here is a failure: a send that quietly succeeds would make the rest of the test vacuous.
        var ex = await Assert.ThrowsAsync<EmailSendException>(() => client.SendAsync(Message(failure), TimeSpan.FromHours(1)));
        observed.AppendLine(ex.ToString());
        Assert.Null(ex.InnerException);

        // The Notify and ACS clients log through their own typed loggers; read those too.
        observed.AppendLine(Dump(clientLogger));
        var health = await provider.CheckAsync(CancellationToken.None);
        observed.AppendLine(health.Message);
        observed.AppendLine(providerLog());
        observed.AppendLine(options.ToString());
        observed.AppendLine(options.Postmark.ToString());
        observed.AppendLine(options.AzureCommunicationServices.ToString());
        observed.AppendLine(options.GovUkNotify.ToString());
        observed.AppendLine(System.Text.Json.JsonSerializer.Serialize(options));
        observed.AppendLine(OutboundEmail.Create(Message(Failure.ServerError), null, "from@example.test", null, 1024, new FakeTimeProvider()).ToString());

        var text = observed.ToString();
        foreach (var sentinel in Sentinels)
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // The mocked factory above never runs IHttpClientFactory's own logging. This one does, at Trace, so it fails if a
    // named client lets the factory log the header that carries the credential (its structured state holds the raw value).
    [Theory]
    [InlineData(EmailProviderKind.Postmark)]
    [InlineData(EmailProviderKind.GovUkNotify)]
    public async Task RealHttpPipeline_LogsNoCredentialHeaderAtTrace(EmailProviderKind kind)
    {
        var logs = new FakeLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).Services.AddSingleton<ILoggerProvider>(logs));
        var options = Options(kind);
                var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(options);
        services.AddSingleton(monitor);
        var server = new Recorder((_, _) => Task.FromResult(Recorder.Json(
            kind == EmailProviderKind.Postmark ? HttpStatusCode.OK : HttpStatusCode.Created,
            kind == EmailProviderKind.Postmark ? """{"ErrorCode":0,"MessageID":"0a129aee-e1cd-480d-b08d-4f48548ff48d"}""" : """{"id":"740e5834-3a29-46b4-9a6f-16142fde533a"}""")));
        string clientName;
        if (kind == EmailProviderKind.Postmark)
        {
            services.AddPostmarkProvider();
            clientName = PostmarkProvider.ClientName;
        }
        else
        {
            services.AddNotifyProvider();
            clientName = NotifyProvider.ClientName;
        }

        // Last registration wins, so the real handler pipeline (logging included) stays and only the socket is replaced.
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => server);
        await using var root = services.BuildServiceProvider();

        var provider = root.GetRequiredService<IEmailProvider>();
        await provider.SendAsync(OutboundEmail.Create(Message(Failure.ServerError), null, "from@example.test", null, 1024, new FakeTimeProvider()), CancellationToken.None);

        Assert.Single(server.Calls);
        var text = string.Join(Environment.NewLine, logs.Collector.GetSnapshot().Select(r => $"{r.Category} {r.Message} {string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])}"));
        Assert.NotEmpty(logs.Collector.GetSnapshot()); // the factory did log something: the assertion below is not vacuous
        foreach (var sentinel in Sentinels)
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(EmailProviderKind.Postmark)]
    [InlineData(EmailProviderKind.AzureCommunicationServices)]
    [InlineData(EmailProviderKind.GovUkNotify)]
    public void InvalidOptions_NameKeysNeverValues(EmailProviderKind kind)
    {
        var options = Options(kind);
        options.TimeoutSeconds = 0;
        options.Postmark.ServerToken = PostmarkToken + "\n";
        options.AzureCommunicationServices.ConnectionString = "endpoint=http://evil.test/;accesskey=" + AcsKeyRaw;
        options.GovUkNotify.ApiKey = "sentinel-key-not-a-real-key";
        options.GovUkNotify.EmailTemplateId = "SENTINEL-ECHO";
        options.GovUkNotify.ReplyToId = "SENTINEL-ECHO";

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        foreach (var sentinel in Sentinels)
        {
            Assert.DoesNotContain(sentinel, result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UnknownProvider_DoesNotEchoTheValue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmailProviderKindParser.Parse(PostmarkToken));

        Assert.DoesNotContain(PostmarkToken, ex.Message);
    }

    private static string Dump(FakeLogger logger)
        => string.Join(
            Environment.NewLine,
            logger.Collector.GetSnapshot().Select(r => $"{r.Category} {r.Message} {r.Exception} {string.Join(' ', r.Scopes)} {string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])}"));

    private static string Dump<T>(FakeLogger<T> logger) => Dump((FakeLogger)logger);
}
