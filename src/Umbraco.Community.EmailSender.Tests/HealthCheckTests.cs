using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.HealthChecks;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.Postmark;

namespace Umbraco.Community.EmailSender.Tests;

public class HealthCheckTests
{
    private static async Task<HealthCheckStatus> Run(params IEmailProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
        {
            services.AddSingleton(provider);
        }

        using var root = services.BuildServiceProvider();
        var statuses = (await new EmailProviderHealthCheck(root).GetStatusAsync()).ToList();
        return Assert.Single(statuses);
    }

    private static PostmarkProvider Postmark(HttpStatusCode status)
    {
        var handler = new Recorder((_, _) => Task.FromResult(Recorder.Json(status, """{"ErrorCode":0,"Message":"SENTINEL-BODY"}""")));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PostmarkProvider.ClientName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.postmarkapp.com/") });
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark };
        options.Postmark.ServerToken = "pm-SENTINEL-TOKEN"; // gitleaks:allow
        monitor.CurrentValue.Returns(options);
        return new PostmarkProvider(factory, monitor, new FakeTimeProvider(), NullLogger<PostmarkProvider>.Instance);
    }

    [Fact]
    public async Task Postmark_200_Success()
    {
        var status = await Run(Postmark(HttpStatusCode.OK));

        Assert.Equal(StatusResultType.Success, status.ResultType);
    }

    [Fact]
    public async Task Postmark_401_ErrorText()
    {
        var status = await Run(Postmark(HttpStatusCode.Unauthorized));

        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.Equal("Postmark rejected the server token.", status.Message);
    }

    [Fact]
    public async Task Acs_BadConnectionString_Error()
    {
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        var options = new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices };
        options.AzureCommunicationServices.ConnectionString = "endpoint=http://evil.test/;accesskey=SENTINEL-ACCESS-KEY"; // gitleaks:allow
        monitor.CurrentValue.Returns(options);
        var provider = new AcsProvider(Substitute.For<IHttpClientFactory>(), monitor, new FakeTimeProvider(), NullLogger<AcsProvider>.Instance);

        var status = await Run(provider);

        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.DoesNotContain("SENTINEL", status.Message);
        Assert.DoesNotContain("evil.test", status.Message);
    }

    [Fact]
    public async Task NoProviderConfigured_ReportsThatCoreSmtpIsInUse()
    {
        var status = await Run();

        Assert.Equal(StatusResultType.Info, status.ResultType);
        Assert.Contains("EmailSender:Provider", status.Message);
    }

    [Fact]
    public async Task ProviderThatThrows_IsAnErrorNamingTheTypeOnly()
    {
        var provider = Substitute.For<IEmailProvider>();
        provider.Name.Returns("Fake");
        provider.CheckAsync(Arg.Any<CancellationToken>()).Returns<Task<HealthCheckStatus>>(_ => throw new InvalidOperationException("SENTINEL-BODY"));

        var status = await Run(provider);

        Assert.Equal(StatusResultType.Error, status.ResultType);
        Assert.Contains("InvalidOperationException", status.Message);
        Assert.DoesNotContain("SENTINEL", status.Message);
    }

    [Fact]
    public void Check_IsDiscoverableInTheServicesGroupWithAStableId()
    {
        var attribute = (HealthCheckAttribute)Attribute.GetCustomAttribute(typeof(EmailProviderHealthCheck), typeof(HealthCheckAttribute))!;

        Assert.Equal("e55002a9-087e-4df5-a0d2-72a8d546fa72", attribute.Id.ToString());
        Assert.Equal("Services", attribute.Group);
        Assert.Equal("Email provider", attribute.Name);
    }

    [Fact]
    public void Check_HasNoActions()
        => Assert.Throws<InvalidOperationException>(() => new EmailProviderHealthCheck(new ServiceCollection().BuildServiceProvider()).ExecuteAction(new HealthCheckAction("x", Guid.Empty)));
}
