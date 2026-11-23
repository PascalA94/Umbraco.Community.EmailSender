using System.Net;
using System.Text;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 21: what only Mailjet does. The shared scenarios live in the conformance suite.</summary>
public class MailjetMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Mailjet");

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public async Task TwoXx_StatusError_IsFailure(HttpStatusCode status)
    {
        var body = """{"Messages":[{"Status":"error","Errors":[{"ErrorCode":"mj-0013","StatusCode":400,"ErrorMessage":"person.SENTINEL@example.test"}]}]}""";
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(status, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailjet rejected the email: HTTP 400, mj-0013.", error?.Message);
        Assert.Single(rig.Server.Calls);
        rig.AssertNoLeaks(error!.Message);
    }

    [Fact]
    public async Task NonTwoXx_WithAllSuccessBody_IsFailure()
    {
        var body = FixtureLoader.Text("mailjet", "response-success.json");
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.InternalServerError, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailjet rejected the email: HTTP 500.", error?.Message);
    }

    [Fact]
    public async Task TwoXx_NoMessagesArray_FailsClosed()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.OK, "{}")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailjet answered HTTP 200 but the result could not be read.", error?.Message);
    }

    [Fact]
    public async Task GlobalError_Mapped()
    {
        var body = FixtureLoader.Text("mailjet", "response-error-global.json");
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.BadRequest, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        // The global error has no Messages wrapper, and its ErrorMessage text is never shown.
        Assert.Equal("Mailjet rejected the email: HTTP 400, mj-0002.", error?.Message);
    }

    [Fact]
    public async Task ForbiddenSender_send0008_Hint()
    {
        var signal = Profile.Expect.UnverifiedSender!;
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(signal.Status, signal.Body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailjet rejected the email: HTTP 403, send-0008. Verify the sender address or domain in Mailjet.", error?.Message);
    }

    [Fact]
    public void SecretKey_InLeakSweep()
    {
        var options = Profile.Options();

        Assert.Contains(options.Mailjet.SecretKey, Profile.Secrets);
        Assert.Contains(options.Mailjet.ApiKey, Profile.Secrets);
        Assert.DoesNotContain(options.Mailjet.SecretKey!, options.ToString() + options.Mailjet);
        Assert.DoesNotContain(options.Mailjet.SecretKey!, System.Text.Json.JsonSerializer.Serialize(options));
        Assert.DoesNotContain(options.Mailjet.ApiKey!, System.Text.Json.JsonSerializer.Serialize(options));
    }

    [Fact]
    public async Task BasicAuth_IsPublicColonSecret()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var auth = rig.Server.Calls[0].Request.Headers.Authorization!;
        var options = Profile.Options().Mailjet;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal($"{options.ApiKey}:{options.SecretKey}", Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!)));
        Assert.Equal("https://api.mailjet.com/v3.1/send", rig.Server.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task OneMessage_AndNoSandboxMode()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { To = ["a@example.test", "b@example.test"] }));

        var root = rig.Recorded().Json!.Value;
        Assert.Equal(1, root.GetProperty("Messages").GetArrayLength());
        Assert.False(root.TryGetProperty("SandboxMode", out _));
        Assert.Equal(2, root.GetProperty("Messages")[0].GetProperty("To").GetArrayLength());
    }

    [Theory]
    [InlineData(null, "s")]
    [InlineData("k", null)]
    [InlineData("", "s")]
    [InlineData("k\n", "s")]
    [InlineData("k", "s\n")]
    public void Keys_MissingOrMalformed_FailsValidationNamingTheKey(string? key, string? secret)
    {
        var options = Profile.Options();
        options.Mailjet.ApiKey = key;
        options.Mailjet.SecretKey = secret;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.All(result.Failures!, f => Assert.StartsWith("EmailSender:Mailjet:", f, StringComparison.Ordinal));
    }
}
