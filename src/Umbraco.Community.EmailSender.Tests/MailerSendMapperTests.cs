using System.Net;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 23: what only MailerSend does. The shared scenarios live in the conformance suite.</summary>
public class MailerSendMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("MailerSend");

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string? retryAfter = null) => FakeProviderServer.Answer(status, body, retryAfter);

    [Fact]
    public async Task Accepted202_IdFromHeaderOnly()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("5e42957d51f1d94a1070a733", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"warnings":[{"type":"ALL_SUPPRESSED"}]}""")]
    public async Task Accepted202_NoMessageId_IsFailure(string body)
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(HttpStatusCode.Accepted, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.NotNull(error);
        Assert.Contains("HTTP 202", error.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    public async Task SendPaused_AcceptedWithWarning()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) =>
        {
            var response = Answer(HttpStatusCode.Accepted, string.Empty);
            response.Headers.TryAddWithoutValidation("x-message-id", "abc123");
            response.Headers.TryAddWithoutValidation("x-send-paused", "true");
            return Task.FromResult(response);
        });

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var warning = Assert.Single(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("paused", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CcOver10_RefusedBeforeSend()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));
        var cc = Enumerable.Range(0, 11).Select(i => $"cc{i}@example.test").ToList();

        var error = await rig.SendAsync(Scenarios.Minimal() with { Cc = cc });

        Assert.Equal("MailerSend accepts at most 10 recipients in Cc.", error?.Message);
        Assert.Empty(rig.Server.Calls);
    }

    [Fact]
    public async Task CustomHeaders_Dropped()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { Headers = new Dictionary<string, string> { ["Expires"] = Scenarios.Expires, ["X-Custom"] = "x" } }));

        Assert.False(rig.Recorded().Json!.Value.TryGetProperty("headers", out _));
    }

    [Fact]
    public async Task Quota429_NotRetried()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(HttpStatusCode.TooManyRequests, """{"message":"quota. #MS42901"}""", "1")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("MailerSend rejected the email: HTTP 429, MS42901.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    [Trait("Fixture", "verbatim")]
    public async Task RateLimit429_Retried()
    {
        var limited = FixtureLoader.Text("mailersend", "response-ratelimit.json");
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1 ? Answer(HttpStatusCode.TooManyRequests, limited, "1") : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
    }

    [Fact]
    public async Task RecipientLimit_MS42205_HasApprovalHint()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(Answer(
            HttpStatusCode.UnprocessableEntity, """{"message":"The given data was invalid.","errors":{"to":["Too many recipients. #MS42205"]}}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("MailerSend rejected the email: HTTP 422, MS42205. MailerSend allows one recipient per field until the account is approved.", error?.Message);
    }

    [Fact]
    public async Task ReplyTo_IsOneObject()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { ReplyTo = ["r@example.test"] }));

        Assert.Equal(System.Text.Json.JsonValueKind.Object, rig.Recorded().Json!.Value.GetProperty("reply_to").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mlsn.key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.MailerSend.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:MailerSend:ApiKey", StringComparison.Ordinal));
    }
}
