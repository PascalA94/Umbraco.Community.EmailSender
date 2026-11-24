using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Bird;
using Umbraco.Community.EmailSender.Providers.Pipeline;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 22: what only Bird does. The shared scenarios live in the conformance suite.</summary>
public class BirdMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Bird");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok()
        => (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile));

    [Fact]
    public async Task Category_AlwaysTransactional()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        // Inline sends default to marketing, which blocks on every suppression and opt-out (design 9.3).
        Assert.Equal("transactional", rig.Recorded().Json!.Value.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Tracking_FalseSentExplicitly()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var json = rig.Recorded().Json!.Value;
        Assert.Equal(JsonValueKind.False, json.GetProperty("track_opens").ValueKind);
        Assert.Equal(JsonValueKind.False, json.GetProperty("track_clicks").ValueKind);
    }

    [Fact]
    public async Task TrackingSwitchOff_SendsNoTrackingFields()
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.DisableLinkTracking = false);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var json = rig.Recorded().Json!.Value;
        Assert.False(json.TryGetProperty("track_opens", out _));
        Assert.False(json.TryGetProperty("track_clicks", out _));
    }

    [Fact]
    public async Task Status421_RegionHint()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(
            HttpStatusCode.MisdirectedRequest, """{"error":{"type":"x","code":"E01010","name":"Misdirected","message":"use eu1"}}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Bird rejected the email: HTTP 421, E01010. The key's region does not match EmailSender:Bird:Region.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Theory]
    [InlineData("us1", "us1.platform.bird.com")]
    [InlineData("eu1", "eu1.platform.bird.com")]
    public async Task Region_UsesTheRightHost(string region, string host)
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.Bird.Region = region);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal($"https://{host}/v1/email/messages", rig.Server.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public void Region_Unknown_FailsValidationNamingTheAllowedValues()
    {
        var options = Profile.Options();
        options.Bird.Region = "ap1";

        Assert.Equal(["EmailSender:Bird:Region must be one of: us1, eu1."], new EmailSenderOptionsValidator().Validate(null, options).Failures);
    }

    [Fact]
    public async Task Attachments21_RefusedBeforeSend()
    {
        using var rig = new ConformanceRig(Profile, Ok());
        var attachments = Enumerable.Range(0, 21).Select(i => new OutboundAttachment($"f{i}.txt", "text/plain", "QQ==")).ToList();

        var error = await rig.SendAsync(Scenarios.Minimal() with { Attachments = attachments });

        Assert.Equal("Bird accepts at most 20 attachments.", error?.Message);
        Assert.Empty(rig.Server.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bk_us1_key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Bird.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Bird:ApiKey", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bk_us1_abc", "us1", false)]
    [InlineData("bm_eu1_abc", "eu1", false)]
    [InlineData("bk_eu1_abc", "us1", true)]
    [InlineData("bm_us1_abc", "eu1", true)]
    [InlineData("abc123", "us1", true)]
    public void KeyPrefix_WarnsOnlyWhenItDoesNotMatchTheRegion(string key, string region, bool warns)
    {
        var options = Profile.Options();
        options.Bird.ApiKey = key;
        options.Bird.Region = region;

        var warnings = new BirdMapper().StartupWarnings(options);

        Assert.Equal(warns, warnings.Count == 1);
        Assert.All(warnings, w => Assert.DoesNotContain(key, w, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyPrefixOtherRegion_LogsWarningAtStartup()
    {
        var options = Profile.Options();
        options.Bird.ApiKey = "bk_eu1_SENTINEL_KEY"; // gitleaks:allow
        options.Bird.Region = "us1";
        var monitor = Substitute.For<IOptionsMonitor<EmailSenderOptions>>();
        monitor.CurrentValue.Returns(options);
        var logger = new FakeLogger<ProviderStartupWarnings>();

        await new ProviderStartupWarnings(monitor, [new BirdMapper()], logger).HandleAsync(
            new UmbracoApplicationStartingNotification(Umbraco.Cms.Core.RuntimeLevel.Run, isRestarting: false), CancellationToken.None);

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("EmailSender:Bird:Region", record.Message);
        Assert.DoesNotContain("SENTINEL", record.Message, StringComparison.Ordinal);
    }
}
