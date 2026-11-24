using System.Net;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Smtp2Go;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 24: what only SMTP2GO does. The shared scenarios live in the conformance suite.</summary>
public class Smtp2GoMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Smtp2Go");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Ok()
        => (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile));

    [Fact]
    [Trait("Fixture", "synthesised")]
    public async Task TwoXx_FailedGreaterThanZero_IsFailure()
    {
        var body = FixtureLoader.Text("smtp2go", "response-200-with-failure.synthesised.json");
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.OK, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.NotNull(error);
        Assert.DoesNotContain("example.com", error.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    public async Task TwoXx_PartlyFailed_SaysTheOthersWereAccepted()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.OK,
            """{"request_id":"x","data":{"succeeded":2,"failed":1,"failures":["x"],"email_id":"abc"}}""")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("SMTP2GO rejected the email: HTTP 200. 1 of 3 recipients failed. The other 2 were accepted: do not resend to them.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Theory]
    [InlineData("""{"request_id":"x","data":{"succeeded":0,"failed":0,"failures":[]}}""")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"data":{"failed":"1"}}""")]
    public async Task TwoXx_NoEvidenceOfSuccess_FailsClosed(string body)
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.OK, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("SMTP2GO answered HTTP 200 but the result could not be read.", error?.Message);
    }

    [Fact]
    public async Task ReplyTo_AsCustomHeader()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { ReplyTo = ["a@example.test", "b@example.test"], Headers = new Dictionary<string, string> { ["X-Custom"] = "x" } }));

        var json = rig.Recorded().Json!.Value;
        Assert.False(json.TryGetProperty("reply_to", out _));
        var headers = json.GetProperty("custom_headers").EnumerateArray().Select(h => (h.GetProperty("header").GetString(), h.GetProperty("value").GetString())).ToList();
        Assert.Contains(("Reply-To", "a@example.test, b@example.test"), headers);
        Assert.Contains(("X-Custom", "x"), headers);
    }

    [Fact]
    public async Task FastAccept_NeverSent()
    {
        using var rig = new ConformanceRig(Profile, Ok());

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        // Without fastaccept a refusal is reported in the response; with it, a 200 would hide the failure.
        Assert.DoesNotContain("fastaccept", System.Text.Encoding.UTF8.GetString(rig.Server.Calls[0].Body), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.ENDPOINT_PERMISSION_DENIED","error":"SENTINEL-ECHO"}}""", HttpStatusCode.Forbidden)]
    [InlineData("""{"error":"SENTINEL-ECHO","error_code":"E_ApiResponseCodes.ENDPOINT_PERMISSION_DENIED"}""", HttpStatusCode.Forbidden)]
    [InlineData("""{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.ENDPOINT_PERMISSION_DENIED","error":"SENTINEL-ECHO"}}""", HttpStatusCode.OK)]
    public async Task ErrorShapes_NestedAndFlat(string body, HttpStatusCode status)
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(status, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Contains("E_ApiResponseCodes.ENDPOINT_PERMISSION_DENIED", error?.Message);
        Assert.DoesNotContain("SENTINEL", error!.Message);
    }

    [Theory]
    [InlineData("Global", "api.smtp2go.com")]
    [InlineData("US", "us-api.smtp2go.com")]
    [InlineData("EU", "eu-api.smtp2go.com")]
    [InlineData("AU", "au-api.smtp2go.com")]
    public async Task Region_UsesTheRightHost(string region, string host)
    {
        using var rig = new ConformanceRig(Profile, Ok(), o => o.Smtp2Go.Region = region);

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal($"https://{host}/v3/email/send", rig.Server.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public void Region_Unknown_FailsValidationNamingTheAllowedValues()
    {
        var options = Profile.Options();
        options.Smtp2Go.Region = "UK";

        Assert.Equal(["EmailSender:Smtp2Go:Region must be one of: Global, US, EU, AU."], new EmailSenderOptionsValidator().Validate(null, options).Failures);
    }

    [Theory]
    [InlineData("api-abc", false)]
    [InlineData("abc", true)]
    public void KeyWithoutApiPrefix_Warns(string key, bool warns)
    {
        var options = Profile.Options();
        options.Smtp2Go.ApiKey = key;

        var warnings = new Smtp2GoMapper().StartupWarnings(options);

        Assert.Equal(warns, warnings.Count == 1);
        Assert.All(warnings, w => Assert.DoesNotContain(key, w, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("api-key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Smtp2Go.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Smtp2Go:ApiKey", StringComparison.Ordinal));
    }
}
