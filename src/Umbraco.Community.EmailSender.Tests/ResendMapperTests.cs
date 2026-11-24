using System.Net;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 18: what only Resend does. The shared scenarios live in the conformance suite.</summary>
public class ResendMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Resend");

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string? retryAfter = null) => FakeProviderServer.Answer(status, body, retryAfter);

    [Theory]
    [InlineData("daily_quota_exceeded")]
    [InlineData("monthly_quota_exceeded")]
    public async Task Quota429_NotRetried(string name)
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(HttpStatusCode.TooManyRequests, $$"""{"statusCode":429,"name":"{{name}}","message":"x"}""", "1")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal($"Resend rejected the email: HTTP 429, {name}.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    [Trait("Fixture", "synthesised")]
    public async Task RateLimit429_Retried()
    {
        var limited = FixtureLoader.Text("resend", "response-ratelimit.synthesised.json");
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1 ? Answer(HttpStatusCode.TooManyRequests, limited, "1") : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
    }

    [Fact]
    public async Task IdempotencyKey_OnEveryAttempt()
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n < 3 ? Answer(HttpStatusCode.ServiceUnavailable, string.Empty, "0") : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var keys = rig.Server.Calls.Select((_, i) => rig.Recorded(i).IdempotencyKey).ToList();
        Assert.Equal(3, keys.Count);
        Assert.NotNull(keys[0]);
        Assert.All(keys, k => Assert.Equal(keys[0], k));
        Assert.True(Guid.TryParse(keys[0], out _));
    }

    [Fact]
    public async Task IdempotencyKey_DiffersBetweenSends()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));
        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.NotEqual(rig.Recorded(0).IdempotencyKey, rig.Recorded(1).IdempotencyKey);
    }

    [Fact]
    public async Task Forbidden403_UnverifiedDomainHint()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(HttpStatusCode.Forbidden, """{"statusCode":403,"name":"validation_error","message":"The x.com domain is not verified"}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal(
            "Resend rejected the email: HTTP 403, validation_error. Verify the sender domain in Resend. Without a verified domain Resend delivers only to the account owner's own address.",
            error?.Message);
    }

    [Fact]
    public async Task RestrictedKey403_PointsAtTheKey()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(HttpStatusCode.Forbidden, """{"statusCode":403,"name":"restricted_api_key","message":"x"}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Resend rejected the email: HTTP 403, restricted_api_key. Check EmailSender:Resend:ApiKey.", error?.Message);
    }

    [Fact]
    public async Task SendsToAsAnArray_AndUsesApiHost()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal("https://api.resend.com/emails", rig.Server.Calls[0].Request.RequestUri!.ToString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, rig.Recorded().Json!.Value.GetProperty("to").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("re_key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Resend.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Resend:ApiKey", StringComparison.Ordinal));
    }
}
