using System.Net;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 20: what only Brevo does. The shared scenarios live in the conformance suite.</summary>
public class BrevoMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Brevo");

    private const string DuplicateBody = """{"code":"duplicate_parameter","message":"SENTINEL-ECHO"}""";

    [Fact]
    public async Task CustomHeaders_Dropped_IdempotencyKeyKept()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { Headers = new Dictionary<string, string> { ["Expires"] = Scenarios.Expires, ["X-Custom"] = "x" } }));

        var recorded = rig.Recorded();
        Assert.Empty(recorded.Headers);
        Assert.True(Guid.TryParse(recorded.IdempotencyKey, out _));
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Debug && r.Message.Contains("2 not sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateOnRetry_TreatedAsAccepted()
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1
            ? FakeProviderServer.Answer(HttpStatusCode.ServiceUnavailable, string.Empty, "0")
            : FakeProviderServer.Answer(HttpStatusCode.BadRequest, DuplicateBody)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Null(error);
        Assert.Equal(2, rig.Server.Calls.Count);
        Assert.Equal(rig.Recorded(0).IdempotencyKey, rig.Recorded(1).IdempotencyKey);
        var logs = rig.Logs.GetSnapshot();
        Assert.Contains(logs, r => r.Level == LogLevel.Warning && r.Message.Contains("first attempt was accepted", StringComparison.Ordinal));
        Assert.Contains(logs, r => r.Level == LogLevel.Information && r.Message.Contains("message id none,", StringComparison.Ordinal));
        rig.AssertNoLeaks();
    }

    // The status of a duplicate is undocumented, so the rule must not depend on 400 (review finding: a wrong guess re-sends an invite).
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task DuplicateOnRetry_AnyClientErrorStatus_TreatedAsAccepted(HttpStatusCode status)
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1
            ? FakeProviderServer.Answer(HttpStatusCode.ServiceUnavailable, string.Empty, "0")
            : FakeProviderServer.Answer(status, DuplicateBody)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));
    }

    [Fact]
    public async Task DuplicateOnFirstAttempt_IsFailure()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.BadRequest, DuplicateBody)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Brevo rejected the email: HTTP 400, duplicate_parameter.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    public async Task ReplyTo_FirstOnly_WarnsCount()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { ReplyTo = ["a@example.test", "b@example.test", "c@example.test"] }));

        Assert.Equal(["a@example.test"], rig.Recorded().ReplyTo);
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Message.Contains("2 more were not sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsesApiKeyHeader_AndApiHost()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var request = rig.Server.Calls[0].Request;
        Assert.Equal("https://api.brevo.com/v3/smtp/email", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(Profile.Secrets[0], Assert.Single(request.Headers.GetValues("api-key")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Brevo.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Brevo:ApiKey", StringComparison.Ordinal));
    }
}
