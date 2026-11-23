using System.Net;
using System.Text;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 19: what only Mailgun does. The shared scenarios live in the conformance suite.</summary>
public class MailgunMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Mailgun");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok()
        => (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile));

    [Fact]
    public async Task Multipart_PartsPerRecipient()
    {
        using var rig = new ConformanceRig(Profile, Ok());
        var email = Scenarios.Minimal() with { To = ["a@example.test", "b@example.test"], Cc = ["c@example.test"], Bcc = ["d@example.test"] };

        Assert.Null(await rig.SendAsync(email));

        var parts = MailgunProfile.Parse(rig.Server.Calls[0].Body);
        Assert.Equal(["a@example.test", "b@example.test"], parts.Where(p => p.Name == "to").Select(p => Encoding.UTF8.GetString(p.Data)));
        Assert.Single(parts, p => p.Name == "cc");
        Assert.Single(parts, p => p.Name == "bcc");
        Assert.StartsWith("multipart/form-data", rig.Server.Calls[0].Request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Status420_Retried()
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1 ? FakeProviderServer.Answer((HttpStatusCode)420, string.Empty, "1") : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
    }

    [Fact]
    public async Task Status420_LongRetryAfter_FailsAtOnceWithRateLimitText()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer((HttpStatusCode)420, string.Empty, "60")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailgun is rate limiting this site (HTTP 420). Try again later.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Theory]
    [InlineData("mg.mailer.test", true)]
    [InlineData("sandbox123.mailgun.org", true)]
    [InlineData("mg.mailer.test/../v3", false)]
    [InlineData("https://mg.mailer.test", false)]
    [InlineData("mg.mailer.test:443", false)]
    [InlineData("mg example.test", false)]
    [InlineData("mg.mailer.test\n", false)]
    [InlineData("localhost", false)]
    [InlineData("", false)]
    public void Domain_Validation(string domain, bool valid)
    {
        var options = Profile.Options();
        options.Mailgun.Domain = domain;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.All(result.Failures!, f => Assert.StartsWith("EmailSender:Mailgun:Domain", f, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Domain_WithSlash_FailsValidation_AndNeverReachesTheUrlIfOptionsChangeLater()
    {
        // Options can reload after start-up validation: the mapper checks again before building the path.
        using var rig = new ConformanceRig(Profile, Ok(), o => o.Mailgun.Domain = "x.example.test/../../v4");

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("EmailSender:Mailgun:Domain must be a bare host name (no scheme, slash or port).", error?.Message);
        Assert.Empty(rig.Server.Calls);
    }

    [Theory]
    [InlineData("US", "api.mailgun.net")]
    [InlineData("EU", "api.eu.mailgun.net")]
    public async Task Region_UsesTheRightHost_AndDomainInThePath(string region, string host)
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.Mailgun.Region = region);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal($"https://{host}/v3/mg.mailer.test/messages", rig.Server.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Region_EU_UsesEuHost()
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.Mailgun.Region = "EU");

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal("api.eu.mailgun.net", rig.Server.Calls[0].Request.RequestUri!.Host);
    }

    [Fact]
    public void Region_Unknown_FailsValidationNamingTheAllowedValues()
    {
        var options = Profile.Options();
        options.Mailgun.Region = "AU";

        Assert.Equal(["EmailSender:Mailgun:Region must be one of: US, EU."], new EmailSenderOptionsValidator().Validate(null, options).Failures);
    }

    [Fact]
    public async Task BasicAuth_UsernameIsApi()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var auth = rig.Server.Calls[0].Request.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal("api:" + Profile.Options().Mailgun.ApiKey, Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!)));
    }

    [Fact]
    public async Task TrackingOff_SendsAllThreeOptions()
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.DisableLinkTracking = true);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var parts = MailgunProfile.Parse(rig.Server.Calls[0].Body).ToDictionary(p => p.Name, p => Encoding.UTF8.GetString(p.Data));
        Assert.Equal("no", parts["o:tracking"]);
        Assert.Equal("no", parts["o:tracking-clicks"]);
        Assert.Equal("no", parts["o:tracking-opens"]);
    }

    [Fact]
    public async Task Attachment_IsAFilePartWithNameAndContentType_NeverInline()
    {
        using var rig = new ConformanceRig(Profile, Ok());
        var email = Scenarios.Minimal() with { Attachments = [new("logo.png", "image/png", "iVBORw0K")] };

        Assert.Null(await rig.SendAsync(email));

        var part = Assert.Single(MailgunProfile.Parse(rig.Server.Calls[0].Body), p => p.FileName is not null);
        Assert.Equal("attachment", part.Name);
        Assert.Equal("image/png", part.ContentType);
        Assert.Equal(Convert.FromBase64String("iVBORw0K"), part.Data);
    }
}
