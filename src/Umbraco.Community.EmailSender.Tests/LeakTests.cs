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
using Umbraco.Community.EmailSender.Providers.Registry;
using Microsoft.Extensions.Http;


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
        // From the registry, so a new provider is swept the moment it is registered (design 9.5, registry guard).
        foreach (var kind in ProviderRegistry.All.Select(d => d.Kind))
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

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, string seconds)
    {
        response.Headers.TryAddWithoutValidation("Retry-After", seconds);
        return response;
    }

    private static Recorder Server(Failure failure) => new((request, _) => failure switch
    {
        Failure.NetworkException => throw new HttpRequestException($"{Address} {Subject} {PostmarkToken} SENTINEL-ECHO"),
        Failure.Unauthorized => Task.FromResult(Echo(request, HttpStatusCode.Unauthorized, false)),
        Failure.Forbidden => Task.FromResult(Echo(request, HttpStatusCode.Forbidden, false)),
        Failure.BadRequest => Task.FromResult(Echo(request, (HttpStatusCode)422, false)),
        // Retry-After past the 10-second ceiling: the pipeline fails at once instead of waiting on the fake clock.
        Failure.TooManyRequests => Task.FromResult(WithRetryAfter(Echo(request, HttpStatusCode.TooManyRequests, false), "60")),
        Failure.MalformedBody => Task.FromResult(Echo(request, HttpStatusCode.InternalServerError, true)),
        _ => Task.FromResult(Echo(request, HttpStatusCode.InternalServerError, false)),
    });

    /// <summary>
    /// The provider exactly as the composer registers it (registry definition, real named client), with the server as the
    /// primary handler of every client. The factory's own logging handlers stay in place.
    /// </summary>
    private static ServiceProvider Build(EmailProviderKind kind, EmailSenderOptions options, Recorder server)
    {
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(options);
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse("2026-11-04T10:00:00Z")));
        services.AddSingleton(monitor);
        ProviderRegistry.Get(kind).Register(services);
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = server));
        return services.BuildServiceProvider();
    }

    private static string Dump(FakeLogCollector collector)
        => string.Join(
            Environment.NewLine,
            collector.GetSnapshot().Select(r => $"{r.Category} {r.Message} {r.Exception} {string.Join(' ', r.Scopes)} {string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])}"));

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
        await using var root = Build(kind, options, Server(failure));
        var provider = root.GetRequiredService<IEmailProvider>();
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
        observed.AppendLine(string.Join(Environment.NewLine, clientLogger.Collector.GetSnapshot().Select(r => $"{r.Message} {r.Exception} {string.Join(' ', r.StructuredState?.Select(kv => kv.Value) ?? [])}")));
        var health = await provider.CheckAsync(CancellationToken.None);
        observed.AppendLine(health.Message);
        observed.AppendLine(Dump(root.GetFakeLogCollector()));
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

    public static TheoryData<EmailProviderKind> Kinds() => new(ProviderRegistry.All.Select(d => d.Kind));

    // The factory logs request headers at Trace, and its structured state holds the raw value. This fails if any registered
    // client lets the factory log the header that carries the credential.
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task RealHttpPipeline_LogsNoCredentialHeaderAtTrace(EmailProviderKind kind)
    {
        var options = Options(kind);
        var profile = Conformance.Profiles.All.Single(p => p.Kind == kind);
        var server = new Recorder((_, _) => Task.FromResult(Conformance.FakeProviderServer.Success(profile)));
        await using var root = Build(kind, options, server);

        var provider = root.GetRequiredService<IEmailProvider>();
        await provider.SendAsync(OutboundEmail.Create(Message(Failure.ServerError), null, "from@example.test", null, 1024, new FakeTimeProvider(), provider.UsesFromAddress), CancellationToken.None);

        Assert.Single(server.Calls);
        var logs = root.GetFakeLogCollector().GetSnapshot();
        Assert.NotEmpty(logs); // something was logged: the assertion below is not vacuous
        var text = Dump(root.GetFakeLogCollector());
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


}
