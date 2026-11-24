using System.Net;
using System.Text.Json;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Mandrill;
using Umbraco.Community.EmailSender.Tests.Conformance;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Task 26: what only Mandrill does. The shared scenarios live in the conformance suite.</summary>
public class MandrillMapperTests
{
    private static readonly IProviderProfile Profile = Profiles.Get("Mandrill");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(string body, HttpStatusCode status = HttpStatusCode.OK)
        => (_, _, _) => Task.FromResult(FakeProviderServer.Answer(status, body));

    [Fact]
    public async Task PerRecipientArray_AnyRejected_IsFailure()
    {
        using var rig = new ConformanceRig(Profile, Respond(
            """[{"email":"a","status":"sent","_id":"1"},{"email":"b","status":"rejected","reject_reason":"hard-bounce","_id":"2"},{"email":"c","status":"sent","_id":"3"}]"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        // 2 of 3 were accepted, so a caller that resends the whole email would send it twice to them.
        Assert.Equal("Mandrill rejected the email: HTTP 200. 1 of 3 recipients rejected (reject_reason: hard-bounce). The other 2 were accepted: do not resend to them.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("queued")]
    [InlineData("scheduled")]
    public async Task QueuedAndScheduled_AreAccepted(string status)
    {
        using var rig = new ConformanceRig(Profile, Respond($$"""[{"email":"a","status":"{{status}}","_id":"abc","queued_reason":"attachments"}]"""));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));
    }

    [Fact]
    public async Task Unsigned_UnverifiedDomainHint()
    {
        using var rig = new ConformanceRig(Profile, Respond("""[{"email":"a","status":"rejected","reject_reason":"unsigned","_id":"1"}]"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mandrill rejected the email: HTTP 200. 1 of 1 recipients rejected (reject_reason: unsigned). Verify the sender domain in Mandrill: it needs SPF and DKIM.", error?.Message);
    }

    [Fact]
    public async Task RejectReason_OutsideTheDocumentedEnum_IsDropped()
    {
        using var rig = new ConformanceRig(Profile, Respond("""[{"email":"a","status":"rejected","reject_reason":"person.SENTINEL@example.test","_id":"1"}]"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal("Mandrill rejected the email: HTTP 200. 1 of 1 recipients rejected.", error?.Message);
    }

    [Theory]
    [InlineData("Invalid_Key", HttpStatusCode.Unauthorized, "Mandrill rejected the email: HTTP 401, Invalid_Key. Check EmailSender:Mandrill:ApiKey.")]
    [InlineData("PaymentRequired", HttpStatusCode.PaymentRequired, "Mandrill rejected the email: HTTP 402, PaymentRequired. Check the Mandrill account's plan and sending blocks.")]
    public async Task ErrorName_NotNumericCode_IsMatched(string name, HttpStatusCode status, string expected)
    {
        // The numeric code is -1 in older clients and 401 in the reference, so it is never read.
        using var rig = new ConformanceRig(Profile, Respond($$"""{"status":"error","code":-1,"name":"{{name}}","message":"SENTINEL-ECHO"}""", status));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal(expected, error?.Message);
    }

    [Fact]
    public async Task KeyInBody_NeverLogged()
    {
        using var rig = new ConformanceRig(Profile, Respond("""[{"email":"a","status":"rejected","reject_reason":"spam","_id":"1"}]"""));

        var error = await rig.SendAsync(Scenarios.Minimal());

        var call = rig.Server.Calls[0];
        var key = Assert.Single(Profile.Secrets);
        Assert.Equal(key, rig.Recorded().Json!.Value.GetProperty("key").GetString());
        Assert.DoesNotContain(key, call.Request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(call.Request.Headers.Select(h => string.Join(' ', h.Value)), v => v.Contains(key, StringComparison.Ordinal));
        rig.AssertNoLeaks(error?.Message);
    }

    [Fact]
    public void RequestTypes_ToStringNeverPrintTheKey()
    {
        var email = Scenarios.Minimal();
        var options = Profile.Options();
        var request = new MandrillMapper().BuildRequest(email, options, new(1, "k"));

        Assert.DoesNotContain(Profile.Secrets[0], request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Profile.Secrets[0], request.Content!.ToString()!, StringComparison.Ordinal);
        var body = new MandrillRequest("md-SECRET", new MandrillMessage("a@example.test", null, [], "s", null, null, null, null, null, null));
        Assert.DoesNotContain("md-SECRET", body.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("md-SECRET", body.Message.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipientTypes_ToCcBcc()
    {
        using var rig = new ConformanceRig(Profile, Respond("""[{"email":"a","status":"sent","_id":"1"}]"""));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { Cc = ["c@example.test"], Bcc = ["b@example.test"] }));

        var to = rig.Recorded().Json!.Value.GetProperty("message").GetProperty("to").EnumerateArray()
            .Select(r => (r.GetProperty("email").GetString(), r.GetProperty("type").GetString())).ToList();
        Assert.Equal([(Scenarios.Address, "to"), ("c@example.test", "cc"), ("b@example.test", "bcc")], to);
    }

    [Fact]
    public async Task InlineImages_NeverSent()
    {
        using var rig = new ConformanceRig(Profile, Respond("""[{"email":"a","status":"sent","_id":"1"}]"""));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal() with { Attachments = [new OutboundAttachment("logo.png", "image/png", "iVBORw0K")] }));

        var message = rig.Recorded().Json!.Value.GetProperty("message");
        Assert.False(message.TryGetProperty("images", out _));
        Assert.Equal(JsonValueKind.Array, message.GetProperty("attachments").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key\n")]
    public void ApiKey_MissingOrMalformed_FailsValidationNamingTheKey(string? key)
    {
        var options = Profile.Options();
        options.Mandrill.ApiKey = key;

        var result = new EmailSenderOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("EmailSender:Mandrill:ApiKey", StringComparison.Ordinal));
    }
}
