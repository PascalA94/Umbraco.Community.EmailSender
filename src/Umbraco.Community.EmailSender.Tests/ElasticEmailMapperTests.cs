using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 25: what only Elastic Email does. The shared scenarios live in the conformance suite.</summary>
public class ElasticEmailMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("ElasticEmail");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok()
        => (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile));

    [Fact]
    public async Task Body_PartTypeHtmlOrPlainText()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { HtmlBody = "<p>h</p>", TextBody = "t" }));

        var parts = rig.Recorded().Json!.Value.GetProperty("Content").GetProperty("Body").EnumerateArray().ToList();
        Assert.Equal(["HTML", "PlainText"], parts.Select(p => p.GetProperty("ContentType").GetString()));
        Assert.All(parts, p => Assert.Equal("utf-8", p.GetProperty("Charset").GetString()));
    }

    [Fact]
    public async Task ReplyTo_CommaString()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { ReplyTo = ["a@example.test", "b@example.test"] }));

        var reply = rig.Recorded().Json!.Value.GetProperty("Content").GetProperty("ReplyTo");
        Assert.Equal(JsonValueKind.String, reply.ValueKind);
        Assert.Equal("a@example.test,b@example.test", reply.GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    public async Task ErrorBody_Ignored_StatusOnly(HttpStatusCode status)
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(
            status, """{"Error":"SENTINEL-ECHO","code":"SOME_CODE","error_code":"OTHER"}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        // The body is undocumented, so no part of it is read.
        Assert.StartsWith($"Elastic Email rejected the email: HTTP {(int)status}.", error?.Message);
        Assert.DoesNotContain("SOME_CODE", error!.Message);
        Assert.DoesNotContain("SENTINEL", error.Message);
    }

    [Fact]
    public async Task IdempotencyKey_Header()
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n < 3 ? FakeProviderServer.Answer(HttpStatusCode.ServiceUnavailable, string.Empty, "0") : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        var keys = rig.Server.Calls.Select((_, i) => rig.Recorded(i).IdempotencyKey).ToList();
        Assert.Equal(3, keys.Count);
        Assert.True(Guid.TryParse(keys[0], out _));
        Assert.All(keys, k => Assert.Equal(keys[0], k));
    }

    [Fact]
    public async Task Status500_RetriedBecauseOfTheKey()
    {
        using var rig = new ConformanceRig(Profile, (n, _, _) => Task.FromResult(n == 1 ? FakeProviderServer.Answer(HttpStatusCode.InternalServerError, string.Empty) : FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.ElasticEmail.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:ElasticEmail:ApiKey", StringComparison.Ordinal));
    }
}
