using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Registry;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>The scenario ids of design 9.5 and the emails they send.</summary>
internal static class Scenarios
{
    public const string Address = "person.SENTINEL@example.test";
    public const string FromAddress = "site.SENTINEL@example.test";
    public const string Subject = "SENTINEL-SUBJECT";
    public const string Body = "SENTINEL-BODY";
    public const string Expires = "Mon, 16 Nov 2026 11:00:00 GMT";

    public static readonly string[] Ids =
    [
        "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "C12",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15",
    ];

    /// <summary>From, one To, subject and a text body; nothing else.</summary>
    public static OutboundEmail Minimal() => new(
        FromAddress, [Address], [], [], [], Subject, null, Body, [], new Dictionary<string, string>());

    /// <summary>An address, subject and body that must never surface outside the request.</summary>
    public static readonly string[] PersonalData = [Address, FromAddress, Subject, Body, "SENTINEL"];
}

/// <summary>
/// One provider wired the way the composer wires it (registry definition, real named client, real pipeline) with the fake
/// server as the primary handler, a fake clock and a fake log collector.
/// </summary>
internal sealed class ConformanceRig : IDisposable
{
    private readonly ServiceProvider services;

    public ConformanceRig(
        IProviderProfile profile,
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        Action<EmailSenderOptions>? tweak = null)
    {
        Profile = profile;
        Server = new FakeProviderServer(Clock, respond, profile.Auxiliary);
        Options = profile.Options();
        tweak?.Invoke(Options);
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(Options);

        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        collection.AddSingleton<TimeProvider>(Clock);
        collection.AddSingleton(monitor);
        ProviderRegistry.Get(profile.Kind).Register(collection);
        // Runs after the provider's own handler set-up, so every named client ends at the fake server.
        collection.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Server));
        services = collection.BuildServiceProvider();
        Provider = services.GetRequiredService<IEmailProvider>();
        Logs = services.GetFakeLogCollector();
    }

    public IProviderProfile Profile { get; }
    public SteppingClock Clock { get; } = new(DateTimeOffset.Parse("2026-11-16T10:00:00Z"));
    public FakeProviderServer Server { get; }
    public EmailSenderOptions Options { get; }
    public IEmailProvider Provider { get; }
    public FakeLogCollector Logs { get; }

    public RecordedEmail Recorded(int index = 0) => Profile.Normalise(Server.Calls[index].Request, Server.Calls[index].Body);

    /// <summary>Sends, moving the fake clock only when the send is waiting on it; every failure must be a clean EmailSendException.</summary>
    public async Task<Exception?> SendAsync(OutboundEmail email)
    {
        var error = await Clock.DriveAsync(Provider.SendAsync(email, CancellationToken.None), () => Server.InFlight);
        if (error is not null)
        {
            Assert.IsType<EmailSendException>(error);
            Assert.Null(error.InnerException);
        }

        return error;
    }

    /// <summary>F15, run after every scenario: no secret or personal data in logs (structured state included) or the error.</summary>
    public void AssertNoLeaks(params string?[] texts)
    {
        var observed = new List<string>();
        foreach (var record in Logs.GetSnapshot())
        {
            observed.Add(record.Message);
            observed.Add(record.Exception?.ToString() ?? string.Empty);
            observed.AddRange(record.StructuredState?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);
        }

        observed.AddRange(texts.Select(t => t ?? string.Empty));
        observed.Add(Options.ToString());
        foreach (var text in observed)
        {
            foreach (var secret in Profile.Secrets.Concat(Scenarios.PersonalData))
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
    }

    public void Dispose() => services.Dispose();
}
