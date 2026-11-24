using System.Net;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 27: what only Mailtrap does. The shared scenarios live in the conformance suite.</summary>
public class MailtrapMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Mailtrap");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(HttpStatusCode status, string body)
        => (_, _, _) => Task.FromResult(FakeProviderServer.Answer(status, body));

    [Fact]
    public async Task TwoXx_SuccessFalse_IsFailure()
    {
        using var rig = new ConformanceRig(Profile, Respond(HttpStatusCode.OK, """{"success":false,"errors":["SENTINEL-ECHO"]}"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailtrap rejected the email: HTTP 200.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Fact]
    public async Task Unauthorized_TokenOrDomainHint()
    {
        using var rig = new ConformanceRig(Profile, Respond(HttpStatusCode.Unauthorized, """{"success":false,"errors":["Unauthorized"]}"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailtrap rejected the email: HTTP 401. Mailtrap answers 401 for a bad token and for an unverified sending domain.", error?.Message);
    }

    [Fact]
    public async Task Forbidden403_VerifyDomainOrPermissions()
    {
        using var rig = new ConformanceRig(Profile, Respond(HttpStatusCode.Forbidden, """{"success":false,"errors":["Forbidden"]}"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mailtrap rejected the email: HTTP 403. Verify the sending domain, or check the token's permissions, in Mailtrap.", error?.Message);
    }

    [Fact]
    public async Task Ids_PerRecipient_FirstLogged()
    {
        using var rig = new ConformanceRig(Profile, Respond(HttpStatusCode.OK, """{"success":true,"message_ids":["id-one","id-two","id-three"]}"""));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { Cc = ["a@example.test"], Bcc = ["b@example.test"] }));

        var sent = Assert.Single(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Contains("message id id-one,", sent.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("id-two", sent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheTransactionalHost_NeverBatchOrSandbox()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal("https://send.api.mailtrap.io/api/send", rig.Server.Calls[0].Request.RequestUri!.ToString());
        Assert.Equal(["send.api.mailtrap.io"], new Umbraco.Community.EmailSender.Providers.Mailtrap.MailtrapMapper().Capabilities.AllowedHosts);
    }

    [Fact]
    public async Task ReplyTo_IsOneObject_AndAttachmentDispositionIsAttachment()
    {
        using var rig = new ConformanceRig(Profile, (_, _, _) => Task.FromResult(FakeProviderServer.Success(Profile)));
        var attachment = new Umbraco.Community.EmailSender.Providers.OutboundAttachment("logo.png", "image/png", "iVBORw0K");

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { ReplyTo = ["r@example.test"], Attachments = [attachment] }));

        var json = rig.Recorded().Json!.Value;
        Assert.Equal(System.Text.Json.JsonValueKind.Object, json.GetProperty("reply_to").ValueKind);
        Assert.Equal("attachment", json.GetProperty("attachments")[0].GetProperty("disposition").GetString());
        Assert.False(json.GetProperty("attachments")[0].TryGetProperty("content_id", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Mailtrap.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Mailtrap:ApiKey", StringComparison.Ordinal));
    }
}
