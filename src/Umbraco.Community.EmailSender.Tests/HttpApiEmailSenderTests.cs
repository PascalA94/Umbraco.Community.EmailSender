using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;

namespace Umbraco.Community.EmailSender.Tests;

public class HttpApiEmailSenderTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));

    private static EmailMessage Message()
        => new("from@example.test", ["to@example.test"], null!, null!, null!, "Subject text", "body", false, null);

    private static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    private sealed class Rig
    {
        public List<string> Calls { get; } = [];
        public IEventAggregator Events { get; } = Substitute.For<IEventAggregator>();
        public IEmailSenderClient Client { get; } = Substitute.For<IEmailSenderClient>();
        public FakeLogger<HttpApiEmailSender> Logger { get; } = new();
        public HttpApiEmailSender Sender { get; }

        // All four arguments, so the 17 line's obsolete overloads are never picked by overload resolution.
        public Task Send(EmailMessage message, string type, bool notify, TimeSpan? expires = null)
            => Sender.SendAsync(message, type, notify, expires);

        public SendEmailNotification? Published { get; private set; }

        public Rig(bool handled = false, GlobalSettings? settings = null, string? optionsFrom = null)
        {
            Events.PublishAsync(Arg.Any<SendEmailNotification>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Published = call.Arg<SendEmailNotification>();
                Calls.Add("notify");
                if (handled)
                {
                    call.Arg<SendEmailNotification>().HandleEmail();
                }

                return Task.CompletedTask;
            });
            Client.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<TimeSpan?>()).Returns(_ =>
            {
                Calls.Add("send");
                return Task.CompletedTask;
            });
            Sender = new HttpApiEmailSender(Events, Client, Monitor(settings ?? new GlobalSettings()), Monitor(new EmailSenderOptions { From = optionsFrom }), Logger);
        }
    }

    [Fact]
    public void CanSendRequiredEmail_TrueWithoutSmtp()
        => Assert.True(new Rig().Sender.CanSendRequiredEmail());

    [Fact]
    public async Task EnableNotification_PublishesFirst()
    {
        var rig = new Rig();

        await rig.Send(Message(), "Invite", true);

        Assert.Equal(["notify", "send"], rig.Calls);
    }

    [Fact]
    public async Task Notification_CarriesMappedModel_DroppingNullsAndBadAddresses()
    {
        var rig = new Rig();
        var message = new EmailMessage(
            "Sender Name <from@example.test>", ["a@example.test", null!, "bad address <<"], ["cc@example.test"], ["bcc@example.test"],
            ["reply@example.test"], "Subject text", "body", true, null);

        await rig.Send(message, "Invite", true);

        var model = rig.Published!.Message;
        Assert.Equal("Invite", rig.Published.EmailType);
        Assert.Equal("from@example.test", model.From!.Address);
        Assert.Equal("Sender Name", model.From.DisplayName);
        Assert.Equal(["a@example.test"], model.To!.Select(a => a!.Address));
        Assert.Equal(["cc@example.test"], model.Cc!.Select(a => a!.Address));
        Assert.Equal(["bcc@example.test"], model.Bcc!.Select(a => a!.Address));
        Assert.Equal(["reply@example.test"], model.ReplyTo!.Select(a => a!.Address));
        Assert.Equal("Subject text", model.Subject);
        Assert.True(model.IsBodyHtml);
    }

    [Fact]
    public async Task Notification_FromFallsBackToSmtpFrom()
    {
        var rig = new Rig(settings: new GlobalSettings { Smtp = new SmtpSettings { From = "smtp@example.test" } });
        var message = new EmailMessage("", ["a@example.test"], null!, null!, null!, "s", "b", false, null);

        await rig.Send(message, "Invite", true);

        Assert.Equal("smtp@example.test", rig.Published!.Message.From!.Address);
    }

    [Fact]
    public async Task Notification_FromFallsBackToEmailSenderFrom_BeforeSmtpFrom()
    {
        var rig = new Rig(settings: new GlobalSettings { Smtp = new SmtpSettings { From = "smtp@example.test" } }, optionsFrom: "opt@example.test");
        var message = new EmailMessage("", ["a@example.test"], null!, null!, null!, "s", "b", false, null);

        await rig.Send(message, "Invite", true);

        Assert.Equal("opt@example.test", rig.Published!.Message.From!.Address);
    }

    [Fact]
    public async Task HandledNotification_SkipsClient()
    {
        var rig = new Rig(handled: true);

        await rig.Send(Message(), "Invite", true);

        Assert.Equal(["notify"], rig.Calls);
    }

    [Fact]
    public async Task NotificationDisabled_StillSends()
    {
        var rig = new Rig();

        await rig.Send(Message(), "ContentNotification", false);

        Assert.Equal(["send"], rig.Calls);
    }

    [Fact]
    public async Task ExpiresAndMessage_PassedToClient()
    {
        var rig = new Rig();
        var message = Message();

        await rig.Send(message, "Invite", false, TimeSpan.FromHours(2));

        await rig.Client.Received(1).SendAsync(message, TimeSpan.FromHours(2));
    }

#if UMBRACO_17
    [Fact]
    [Obsolete("Exercises the obsolete overloads that exist only on the 17 line.")]
    public async Task ObsoleteOverloads_DelegateWithDefaults()
    {
        var rig = new Rig();
        var message = Message();

        await rig.Sender.SendAsync(message, "Invite");
        await rig.Sender.SendAsync(message, "Invite", true);

        Assert.Equal(["send", "notify", "send"], rig.Calls);
        await rig.Client.Received(2).SendAsync(message, null);
    }
#endif

    [Fact]
    public async Task Handled_LogsNoSubject()
    {
        var rig = new Rig(handled: true);

        await rig.Send(Message(), "Invite", true);

        Assert.DoesNotContain(rig.Logger.Collector.GetSnapshot(), r => r.Message.Contains("Subject text"));
    }
}

public class HttpApiEmailSenderClientTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));

    private static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    private static (HttpApiEmailSenderClient Client, IEmailProvider Provider, FakeLogger<HttpApiEmailSenderClient> Logger) Build(
        EmailSenderOptions? options = null, GlobalSettings? settings = null)
    {
        var provider = Substitute.For<IEmailProvider>();
        provider.Name.Returns("Fake");
        var logger = new FakeLogger<HttpApiEmailSenderClient>();
        var client = new HttpApiEmailSenderClient(
            Monitor(options ?? new EmailSenderOptions()), Monitor(settings ?? new GlobalSettings()), provider, Clock, logger);
        return (client, provider, logger);
    }

    private static EmailMessage Message(string? from = "from@example.test")
        => new(from!, ["to@example.test"], null!, null!, null!, "Subject text", "body", false, null);

    [Fact]
    public async Task Client_PassesExpiresThrough()
    {
        var (client, provider, _) = Build();

        await client.SendAsync(Message(), TimeSpan.FromHours(1));

        await provider.Received(1).SendAsync(
            Arg.Is<OutboundEmail>(e => e.Headers["Expires"] == "Wed, 04 Nov 2026 11:00:00 GMT"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Client_UsesOptionsFromFallback()
    {
        var (client, provider, _) = Build(new EmailSenderOptions { From = "opt@example.test" });

        await client.SendAsync(Message(from: null), null);

        await provider.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.From == "opt@example.test"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Client_PassesOptionsAttachmentCap_AndSendsNothingWhenExceeded()
    {
        var (client, provider, _) = Build(new EmailSenderOptions { MaxTotalAttachmentBytes = 10 });
        var message = new EmailMessage("from@example.test", ["to@example.test"], null!, null!, null!, "s", "b", false,
            [new EmailMessageAttachment(new MemoryStream(new byte[11]), "a.bin")]);

        await Assert.ThrowsAsync<EmailSendException>(() => client.SendAsync(message, null));

        await provider.DidNotReceive().SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Client_AttachmentAtExactlyTheCap_IsSent()
    {
        var (client, provider, _) = Build(new EmailSenderOptions { MaxTotalAttachmentBytes = 10 });
        var message = new EmailMessage("from@example.test", ["to@example.test"], null!, null!, null!, "s", "b", false,
            [new EmailMessageAttachment(new MemoryStream(new byte[10]), "a.bin")]);

        await client.SendAsync(message, null);

        await provider.Received(1).SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Client_MappingFailure_IsLoggedAtWarningWithoutAddresses()
    {
        var (client, _, logger) = Build();
        var message = new EmailMessage("from@example.test", ["bad address <<"], null!, null!, null!, "s", "b", false, null);

        await Assert.ThrowsAsync<EmailSendException>(() => client.SendAsync(message, null));

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.DoesNotContain("bad address", record.Message);
    }

    [Fact]
    public async Task Client_UnexpectedProviderException_IsWrappedWithoutInnerOrMessage()
    {
        var (client, provider, logger) = Build();
        provider.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("echoed to@example.test and a body")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => client.SendAsync(Message(), null));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("example.test", ex.Message);
        Assert.Contains("HttpRequestException", ex.Message);
        Assert.All(logger.Collector.GetSnapshot(), r => Assert.DoesNotContain("example.test", r.Message));
    }

    [Fact]
    public async Task Client_SmtpFromIsLastFallback()
    {
        var (client, provider, _) = Build(settings: new GlobalSettings { Smtp = new SmtpSettings { From = "smtp@example.test" } });

        await client.SendAsync(Message(from: null), null);

        await provider.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.From == "smtp@example.test"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Client_LogsCountsOnly()
    {
        var (client, _, logger) = Build();

        await client.SendAsync(Message(), null);

        var line = Assert.Single(logger.Collector.GetSnapshot()).Message;
        Assert.Contains("Fake", line);
        Assert.DoesNotContain("example.test", line);
        Assert.DoesNotContain("Subject text", line);
    }

    [Fact]
    public async Task Client_ProviderFailureLogsStatusOnlyAndRethrows()
    {
        var (client, provider, logger) = Build();
        provider.SendAsync(Arg.Any<OutboundEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new EmailSendException("Fake rejected the email: HTTP 500.")));

        var ex = await Assert.ThrowsAsync<EmailSendException>(() => client.SendAsync(Message(), null));

        Assert.Equal("Fake rejected the email: HTTP 500.", ex.Message);
        Assert.All(logger.Collector.GetSnapshot(), r => Assert.DoesNotContain("example.test", r.Message));
    }

#if UMBRACO_17
    [Fact]
    [Obsolete("Exercises the obsolete overload that exists only on the 17 line.")]
    public async Task ObsoleteOverload_DelegatesWithNullExpiry()
    {
        var (client, provider, _) = Build();

        await ((IEmailSenderClient)client).SendAsync(Message());

        await provider.Received(1).SendAsync(Arg.Is<OutboundEmail>(e => e.Headers.Count == 0), Arg.Any<CancellationToken>());
    }
#endif
}

public class SmtpOverlapWarningTests
{
    private static async Task<FakeLogger<SmtpOverlapStartupWarning>> Run(GlobalSettings settings)
    {
        var monitor = Substitute.For<IOptionsMonitor<GlobalSettings>>();
        monitor.CurrentValue.Returns(settings);
        var logger = new FakeLogger<SmtpOverlapStartupWarning>();
        await new SmtpOverlapStartupWarning(monitor, logger).HandleAsync(
            new UmbracoApplicationStartingNotification(Umbraco.Cms.Core.RuntimeLevel.Run, isRestarting: false), CancellationToken.None);
        return logger;
    }

    [Fact]
    public async Task SmtpAlsoConfigured_LogsOneWarningAtStartup()
    {
        var logger = await Run(new GlobalSettings { Smtp = new SmtpSettings { Host = "smtp.example.test", From = "a@example.test" } });

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("EmailSender:Provider", record.Message);
        Assert.DoesNotContain("smtp.example.test", record.Message);
    }

    [Fact]
    public async Task PickupDirectoryAlsoConfigured_LogsOneWarning()
    {
        var logger = await Run(new GlobalSettings { Smtp = new SmtpSettings { PickupDirectoryLocation = "/tmp/pickup", From = "a@example.test" } });

        Assert.Single(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task NoSmtp_LogsNothing()
        => Assert.Empty((await Run(new GlobalSettings())).Collector.GetSnapshot());
}
