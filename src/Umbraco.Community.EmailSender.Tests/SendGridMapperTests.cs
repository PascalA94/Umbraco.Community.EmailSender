using System.Net;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 17: what only SendGrid does. The shared scenarios live in the conformance suite.</summary>
public class SendGridMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("SendGrid");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok(Action<HttpResponseMessage>? tweak = null)
        => (_, _, _) =>
        {
            var response = FakeProviderServer.Success(Profile);
            tweak?.Invoke(response);
            return Task.FromResult(response);
        };

    [Fact]
    public async Task FromName_NonAscii_Dropped()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { From = $"Café Zoë <{Scenarios.FromAddress}>" }));

        var recorded = rig.Recorded();
        Assert.Equal(Scenarios.FromAddress, recorded.FromAddress);
        Assert.Null(recorded.FromName);
    }

    [Theory]
    [InlineData("Global", "api.sendgrid.com")]
    [InlineData("EU", "api.eu.sendgrid.com")]
    [InlineData("eu", "api.eu.sendgrid.com")]
    public async Task Region_UsesTheRightHost(string region, string host)
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.SendGrid.Region = region);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal($"https://{host}/v3/mail/send", rig.Server.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Region_EU_UsesEuHost()
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.SendGrid.Region = "EU");

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal("api.eu.sendgrid.com", rig.Server.Calls[0].Request.RequestUri!.Host);
    }

    [Fact]
    public async Task EmptyBody_RefusedBeforeSend()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        var error = await rig.SendAsync(Scenarios.Minimal() with { TextBody = "", HtmlBody = null });

        Assert.Equal("The email has no body, and SendGrid refuses an empty one.", error?.Message);
        Assert.Empty(rig.Server.Calls);
    }

    [Fact]
    public async Task Accepted202_NoBody_IdFromHeaderOrNone()
    {
        using var withHeader = new ConformanceRig(Profile, Ok(r => r.Headers.TryAddWithoutValidation("X-Message-Id", "sg-ID-1")));
        Assert.Null(await withHeader.SendAsync(Scenarios.Minimal()));
        Assert.Contains(withHeader.Logs.GetSnapshot(), r => r.Message.Contains("message id sg-ID-1,", StringComparison.Ordinal));

        using var without = new ConformanceRig(Profile, Ok());
        Assert.Null(await without.SendAsync(Scenarios.Minimal()));
        Assert.Contains(without.Logs.GetSnapshot(), r => r.Message.Contains("message id none,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejection_NeverEchoesProviderMessage()
    {
        var body = """{"errors":[{"message":"person.SENTINEL@example.test is not valid","field":"to"}]}""";
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.BadRequest, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("SendGrid rejected the email: HTTP 400.", error?.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.SendGrid.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:SendGrid:ApiKey", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Failures!, f => f.Contains("key\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Region_Unknown_FailsValidationNamingTheAllowedValues()
    {
        var options = Profile.Options();
        options.SendGrid.Region = "Mars";

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.Equal(["EmailSender:SendGrid:Region must be one of: Global, EU."], result.Failures);
    }
}
