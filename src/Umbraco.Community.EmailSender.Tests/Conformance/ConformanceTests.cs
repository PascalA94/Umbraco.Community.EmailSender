using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>
/// Design 9.5: every provider profile against every scenario. A provider is done when its profile passes all of them, or
/// names a reason for each one that cannot apply. The leak sweep (F15) runs after every scenario.
/// </summary>
public class ConformanceTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var profile in Profiles.All)
        {
            foreach (var id in Scenarios.Ids)
            {
                data.Add(profile.Kind.ToString(), id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Scenario(string kind, string id)
    {
        var profile = Profiles.Get(kind);
        if (profile.NotApplicable.TryGetValue(id, out var reason))
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{kind} {id} is not applicable without a reason.");
            return;
        }

        await (id switch
        {
            "C1" => C1(profile),
            "C2" => C2(profile),
            "C3" => C3(profile),
            "C4" => C4(profile),
            "C5" => C5(profile),
            "C6" => C6(profile),
            "C7" => C7(profile),
            "C8" => C8(profile),
            "C9" => C9(profile),
            "C10" => C10(profile),
            "C11" => C11(profile),
            "C12" => C12(profile),
            "F1" => Rejects(profile, profile.Expect.Auth),
            "F2" => F2(profile),
            "F3" => Rejects(profile, profile.Expect.UnverifiedSender ?? throw NoSignal(kind, id)),
            "F4" => F4(profile),
            "F5" => F5(profile),
            "F6" => F6(profile),
            "F7" => F7(profile),
            "F8" => Rejects(profile, profile.Expect.FailureIn2xx ?? throw NoSignal(kind, id)),
            "F9" => F9(profile),
            "F10" => F10(profile),
            "F11" => F11(profile),
            "F12" => F12(profile),
            "F13" => F13(profile),
            "F14" => F14(profile),
            "F15" => F15(profile),
            _ => throw new InvalidOperationException(id),
        });
    }

    private static Exception NoSignal(string kind, string id) => new InvalidOperationException($"{kind} has no signal for {id}: add it or mark it not applicable.");

    private static Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Success(IProviderProfile profile)
        => (_, _, _) => Task.FromResult(SuccessResponse(profile));

    private static HttpResponseMessage SuccessResponse(IProviderProfile profile) => FakeProviderServer.Success(profile);

    private static string SignalBody(IProviderProfile profile, ResponseSignal signal)
        => signal.Body.EndsWith(".json", StringComparison.Ordinal) ? FixtureLoader.Text(profile.FixtureFolder!, signal.Body) : signal.Body;

    private static string SuccessBody(IProviderProfile profile)
        => profile.Expect.SuccessBody.TrimStart().StartsWith('{') || profile.Expect.SuccessBody.TrimStart().StartsWith('[')
            ? profile.Expect.SuccessBody
            : FixtureLoader.Text(profile.FixtureFolder!, profile.Expect.SuccessBody);

    private static async Task<(ConformanceRig Rig, RecordedEmail Recorded)> SendOkAsync(IProviderProfile profile, OutboundEmail email, Action<Umbraco.Community.EmailSender.Configuration.EmailSenderOptions>? tweak = null)
    {
        var rig = new ConformanceRig(profile, Success(profile), tweak);
        var error = await rig.SendAsync(email);
        Assert.Null(error);
        rig.AssertNoLeaks();
        Assert.Single(rig.Server.Calls);
        return (rig, rig.Recorded());
    }

    private static void AssertShape(IProviderProfile profile, RecordedEmail recorded)
    {
        if (profile.FixtureFolder is null)
        {
            return;
        }

        if (recorded.PartNames is { } parts)
        {
            // A multipart request: the fixtures are curl commands, so part names are compared, not JSON paths.
            var known = FixtureLoader.CurlPartNames(profile.FixtureFolder!);
            var extra = parts.Where(n => !known.Contains(n) && !profile.Expect.ShapeExtras.Any(e => n.StartsWith(e, StringComparison.Ordinal))).ToList();
            Assert.True(extra.Count == 0, $"Sent but not in any {profile.FixtureFolder} request fixture: {string.Join(", ", extra)}");
            foreach (var required in profile.Expect.RequiredPaths)
            {
                Assert.Contains(required, parts);
            }

            return;
        }

        Assert.True(recorded.Json is not null, "The request body is not JSON, so it cannot be shape-matched.");
        var fixture = FixtureLoader.RequestShapes(profile.FixtureFolder!);
        var sent = FixtureLoader.Shape(recorded.Json!.Value);
        var unknown = sent.Where(s => !fixture.Contains(s) && !profile.Expect.ShapeExtras.Any(e => s.StartsWith(e, StringComparison.Ordinal))).ToList();
        Assert.True(unknown.Count == 0, $"Sent but not in any {profile.FixtureFolder} request fixture: {string.Join(", ", unknown)}");
        foreach (var required in profile.Expect.RequiredPaths)
        {
            Assert.Contains(sent, s => s.StartsWith(required + ":", StringComparison.Ordinal));
        }
    }

    // C1: shape match with the fixtures; the credential only where the provider documents it.
    private static async Task C1(IProviderProfile profile)
    {
        var (rig, recorded) = await SendOkAsync(profile, Scenarios.Minimal());
        using var _ = rig;

        Assert.Equal([Scenarios.Address], recorded.To);
        Assert.Equal(Scenarios.FromAddress, recorded.FromAddress ?? Scenarios.FromAddress);
        Assert.Equal(Scenarios.Subject, recorded.Subject);
        Assert.Equal(Scenarios.Body, recorded.Text);
        AssertShape(profile, recorded);

        var (request, body, _) = rig.Server.Calls[0];
        var found = new List<string>();
        foreach (var secret in profile.Secrets)
        {
            found.AddRange(request.Headers.Where(h => h.Value.Any(v => v.Contains(secret, StringComparison.Ordinal))).Select(h => $"header:{h.Key}"));
            if (System.Text.Encoding.UTF8.GetString(body).Contains(secret, StringComparison.Ordinal))
            {
                found.Add("body");
            }

            Assert.DoesNotContain(secret, request.RequestUri!.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(profile.Expect.CredentialLocations.Order(), found.Distinct().Order());
    }

    // C2: the HTML field is used and no empty text part is sent.
    private static async Task C2(IProviderProfile profile)
    {
        var (rig, recorded) = await SendOkAsync(profile, Scenarios.Minimal() with { HtmlBody = $"<p>{Scenarios.Body}</p>", TextBody = null });
        using var _ = rig;

        Assert.Contains(Scenarios.Body, profile.Expect.HtmlAsText ? recorded.Text : recorded.Html);
        Assert.True(recorded.Text is null || recorded.Text.Length > 0, "An empty text part was sent.");
    }

    // C3: a display name on From is kept, or dropped where the provider documents it.
    private static async Task C3(IProviderProfile profile)
    {
        var (rig, recorded) = await SendOkAsync(profile, Scenarios.Minimal() with { From = $"Site Name <{Scenarios.FromAddress}>" });
        using var _ = rig;

        Assert.Equal(Scenarios.FromAddress, recorded.FromAddress);
        Assert.Equal(profile.Expect.KeepsDisplayName ? "Site Name" : null, recorded.FromName);
    }

    // C4: Cc, Bcc and two Reply-Tos; one-address providers send the first and warn.
    private static async Task C4(IProviderProfile profile)
    {
        var email = Scenarios.Minimal() with { Cc = ["cc@example.test"], Bcc = ["bcc@example.test"], ReplyTo = ["r1@example.test", "r2@example.test"] };
        var (rig, recorded) = await SendOkAsync(profile, email);
        using var _ = rig;

        Assert.Equal(["cc@example.test"], recorded.Cc);
        Assert.Equal(["bcc@example.test"], recorded.Bcc);
        var warnings = rig.Logs.GetSnapshot().Count(r => r.Level == LogLevel.Warning);
        switch (profile.Expect.ReplyTo)
        {
            case ReplyToStyle.Single:
                Assert.Equal(["r1@example.test"], recorded.ReplyTo);
                Assert.Equal(1, warnings);
                break;
            case ReplyToStyle.NotSupported:
                Assert.Empty(recorded.ReplyTo);
                Assert.Equal(1, warnings);
                break;
            default:
                Assert.Equal(["r1@example.test", "r2@example.test"], recorded.ReplyTo);
                Assert.Equal(0, warnings);
                break;
        }
    }

    // C5: the Expires header core adds, under the provider's header policy.
    private static async Task C5(IProviderProfile profile)
    {
        var (rig, recorded) = await SendOkAsync(profile, Scenarios.Minimal() with { Headers = new Dictionary<string, string> { ["Expires"] = Scenarios.Expires } });
        using var _ = rig;

        if (profile.Expect.Headers == HeaderPolicy.SendAsGiven)
        {
            Assert.Equal(Scenarios.Expires, Assert.Contains("Expires", recorded.Headers));
        }
        else
        {
            Assert.DoesNotContain("Expires", recorded.Headers.Keys);
        }
    }

    // C6: one attachment round-trips name, type and content, and matches the attachment fixture's shape.
    private static async Task C6(IProviderProfile profile)
    {
        var email = Scenarios.Minimal() with { Attachments = [new OutboundAttachment("report.pdf", "application/pdf", "QUJDRA==")] };
        var (rig, recorded) = await SendOkAsync(profile, email);
        using var _ = rig;

        Assert.Equal(new RecordedAttachment("report.pdf", "application/pdf", "QUJDRA=="), Assert.Single(recorded.Attachments));
        AssertShape(profile, recorded);
    }

    // C7: an image goes as a normal attachment, never inline; the capability says how inline would be addressed.
    private static async Task C7(IProviderProfile profile)
    {
        var email = Scenarios.Minimal() with { Attachments = [new OutboundAttachment("logo.png", "image/png", "iVBORw0K")] };
        var (rig, recorded) = await SendOkAsync(profile, email);
        using var _ = rig;

        Assert.False(Assert.Single(recorded.Attachments).Inline);
        Assert.Equal(profile.Expect.Inline, Capabilities(rig).Inline);
    }

    // C8: tracking off is sent per request where the provider allows it, and nothing is sent where it is an account setting.
    private static async Task C8(IProviderProfile profile)
    {
        var (rig, recorded) = await SendOkAsync(profile, Scenarios.Minimal(), o => o.DisableLinkTracking = true);
        using var _ = rig;

        Assert.Equal(profile.Expect.Tracking == TrackingStyle.PerRequest ? true : null, recorded.TrackingOff);
        Assert.Equal(profile.Expect.Tracking, Capabilities(rig).Tracking);
    }

    // C9: a Unicode subject and file name arrive intact.
    private static async Task C9(IProviderProfile profile)
    {
        const string subject = "Café – menu ☕ ünïcödé";
        var email = Scenarios.Minimal() with { Subject = subject };
        if (profile.Expect.SupportsAttachments)
        {
            email = email with { Attachments = [new OutboundAttachment("Café menu ☕.pdf", "application/pdf", "QUJD")] };
        }

        var (rig, recorded) = await SendOkAsync(profile, email);
        using var _ = rig;

        Assert.Equal(subject, recorded.Subject);
        if (profile.Expect.SupportsAttachments)
        {
            Assert.Equal("Café menu ☕.pdf", Assert.Single(recorded.Attachments).Name);
        }
    }

    // C10: the id is read from the documented place and logged; "none" where the provider returns none.
    private static async Task C10(IProviderProfile profile)
    {
        var (rig, _) = await SendOkAsync(profile, Scenarios.Minimal());
        using var __ = rig;

        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains($"message id {profile.Expect.MessageId},", StringComparison.Ordinal));
    }

    // C11: the same idempotency key on every attempt where the provider supports one, and no key elsewhere.
    private static async Task C11(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (n, _, _) => Task.FromResult(n == 1
            ? FakeProviderServer.Answer(HttpStatusCode.ServiceUnavailable, string.Empty, "0")
            : SuccessResponse(profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
        var keys = new[] { rig.Recorded(0).IdempotencyKey, rig.Recorded(1).IdempotencyKey };
        if (profile.Expect.Idempotency == IdempotencyCarriage.None)
        {
            Assert.All(keys, Assert.Null);
        }
        else
        {
            Assert.NotNull(keys[0]);
            Assert.Equal(keys[0], keys[1]);
        }

        Assert.Equal(profile.Expect.Idempotency, Capabilities(rig).Idempotency);
        rig.AssertNoLeaks();
    }

    // C12: User-Agent and Accept on every request.
    private static async Task C12(IProviderProfile profile)
    {
        var (rig, _) = await SendOkAsync(profile, Scenarios.Minimal());
        using var __ = rig;

        var request = rig.Server.Calls[0].Request;
        Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "Umbraco.Community.EmailSender");
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
    }

    // F1, F3, F8: a refusal (or a failure hidden in a 2xx) is an EmailSendException with the expected text, sent once.
    private static async Task Rejects(IProviderProfile profile, ResponseSignal signal)
    {
        var body = SignalBody(profile, signal);
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(signal.Status, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.NotNull(error);
        Assert.Contains(signal.ExpectedText, error.Message, StringComparison.Ordinal);
        Assert.Single(rig.Server.Calls);
        rig.AssertNoLeaks(error.Message);
    }

    // F2: status and allow-listed code only; never the provider's message.
    private static async Task F2(IProviderProfile profile)
    {
        var signal = profile.Expect.Validation;
        var body = SignalBody(profile, signal);
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(signal.Status, body)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal(signal.ExpectedText, error?.Message);
        rig.AssertNoLeaks(error!.Message);
    }

    // F4: a 429 with Retry-After 1 waits once, then succeeds and logs 2 attempts.
    private static async Task F4(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (n, _, _) => Task.FromResult(n == 1
            ? FakeProviderServer.Answer(HttpStatusCode.TooManyRequests, string.Empty, "1")
            : SuccessResponse(profile)));

        Assert.Null(await rig.SendAsync(Scenarios.Minimal()));

        Assert.Equal(2, rig.Server.Calls.Count);
        Assert.InRange((rig.Server.Calls[1].At - rig.Server.Calls[0].At).TotalSeconds, 1.0, 1.11);
        Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("2 attempts", StringComparison.Ordinal));
        rig.AssertNoLeaks();
    }

    // F5: a long Retry-After never sleeps the request thread.
    private static async Task F5(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(HttpStatusCode.TooManyRequests, string.Empty, "60")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal($"{rig.Provider.Name} is rate limiting this site (HTTP 429). Try again later.", error?.Message);
        Assert.Single(rig.Server.Calls);
        Assert.Equal(rig.Server.Calls[0].At, rig.Clock.GetUtcNow());
        rig.AssertNoLeaks(error!.Message);
    }

    // F6: a quota 429 is not retried.
    private static async Task F6(IProviderProfile profile)
    {
        var signal = profile.Expect.Quota ?? throw NoSignal(profile.Kind.ToString(), "F6");
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(signal.Status, signal.Body, "1")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Contains(signal.ExpectedText, error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    // F7: a 500 is retried only where an idempotency key makes it safe, with the same key.
    private static async Task F7(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (n, _, _) => Task.FromResult(n == 1
            ? FakeProviderServer.Answer(HttpStatusCode.InternalServerError, string.Empty)
            : SuccessResponse(profile)));

        var error = await rig.SendAsync(Scenarios.Minimal());

        if (profile.Expect.Idempotency == IdempotencyCarriage.None)
        {
            Assert.Equal($"{rig.Provider.Name} rejected the email: HTTP 500.", error?.Message);
            Assert.Single(rig.Server.Calls);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(2, rig.Server.Calls.Count);
            Assert.Equal(rig.Recorded(0).IdempotencyKey, rig.Recorded(1).IdempotencyKey);
        }

        rig.AssertNoLeaks(error?.Message);
    }

    // F9: an unparseable 2xx fails closed where failures hide in 2xx bodies, and is accepted with no id elsewhere.
    private static async Task F9(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(FakeProviderServer.Answer(profile.Expect.SuccessStatus, "<html>SENTINEL-ECHO")));

        var error = await rig.SendAsync(Scenarios.Minimal());

        if (profile.Expect.FailClosedOn2xx)
        {
            Assert.Equal($"{rig.Provider.Name} answered HTTP {(int)profile.Expect.SuccessStatus} but the result could not be read.", error?.Message);
        }
        else
        {
            Assert.Null(error);
            Assert.Contains(rig.Logs.GetSnapshot(), r => r.Level == LogLevel.Information && r.Message.Contains("message id none,", StringComparison.Ordinal));
        }

        Assert.Equal(profile.Expect.FailClosedOn2xx, Capabilities(rig).FailClosedOn2xx);
        rig.AssertNoLeaks(error?.Message);
    }

    // F10: 51 recipients, and one over each lower cap, are refused before any request.
    private static async Task F10(IProviderProfile profile)
    {
        var emails = new List<OutboundEmail>
        {
            Scenarios.Minimal() with { To = [.. Enumerable.Range(0, 51).Select(i => $"u{i}@example.test")] },
        };
        if (profile.Expect.MaxCc is { } cc)
        {
            emails.Add(Scenarios.Minimal() with { Cc = [.. Enumerable.Range(0, cc + 1).Select(i => $"c{i}@example.test")] });
        }

        if (profile.Expect.MaxBcc is { } bcc)
        {
            emails.Add(Scenarios.Minimal() with { Bcc = [.. Enumerable.Range(0, bcc + 1).Select(i => $"b{i}@example.test")] });
        }

        foreach (var email in emails)
        {
            using var rig = new ConformanceRig(profile, Success(profile));
            var error = await rig.SendAsync(email);
            Assert.Contains("at most", error?.Message);
            Assert.Empty(rig.Server.Calls);
            rig.AssertNoLeaks(error!.Message);
        }
    }

    // F11: a request over the provider's size cap is refused before any request, naming the limit.
    private static async Task F11(IProviderProfile profile)
    {
        var max = profile.Expect.MaxRequestBytes ?? throw NoSignal(profile.Kind.ToString(), "F11");
        var raw = new byte[(profile.Expect.BinaryAttachments ? max : max * 3 / 4) + 1024];
        using var rig = new ConformanceRig(profile, Success(profile));

        var error = await rig.SendAsync(Scenarios.Minimal() with { Attachments = [new OutboundAttachment("big.bin", "application/octet-stream", Convert.ToBase64String(raw))] });

        Assert.Contains(ProviderPreChecks.FormatSize(max), error?.Message);
        Assert.Empty(rig.Server.Calls);
        Assert.Equal(max, Capabilities(rig).MaxRequestBytes);
    }

    // F12: a hang ends at the budget with timeout text and no retry.
    private static async Task F12(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }, o => o.TimeoutSeconds = 5);

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Equal($"{rig.Provider.Name} did not answer within 5 seconds.", error?.Message);
        Assert.Single(rig.Server.Calls);
    }

    // F13: a redirect is not followed, so the credential never reaches another host.
    private static async Task F13(IProviderProfile profile)
    {
        using var rig = new ConformanceRig(profile, (_, _, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example.test/steal");
            return Task.FromResult(redirect);
        });

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.Contains("HTTP 302", error?.Message);
        Assert.DoesNotContain("evil", error!.Message);
        Assert.Single(rig.Server.Calls);
    }

    // F14: a 1 MB response is read only to 64 KB, without a crash.
    private static async Task F14(IProviderProfile profile)
    {
        var stream = new CountingStream(1024 * 1024);
        using var rig = new ConformanceRig(profile, (_, _, _) => Task.FromResult(new HttpResponseMessage(profile.Expect.SuccessStatus) { Content = new StreamContent(stream) }));

        var error = await rig.SendAsync(Scenarios.Minimal());

        Assert.InRange(stream.Consumed, 1, ResponseReader.MaxBytes);
        Assert.Equal(profile.Expect.FailClosedOn2xx, error is not null);
    }

    // F15: the server echoes every secret and personal value; none comes back in logs, the error or the health-check text.
    private static async Task F15(IProviderProfile profile)
    {
        var echo = string.Join(' ', profile.Secrets.Concat(Scenarios.PersonalData));
        var body = $$"""{"ErrorCode":300,"code":"x","message":"{{echo}}","Message":"{{echo}}","errors":[{"error":"BadRequestError","message":"{{echo}}"}]}""";
        using var rig = new ConformanceRig(profile, (_, request, _) =>
        {
            var response = FakeProviderServer.Answer(HttpStatusCode.BadRequest, body);
            response.Headers.TryAddWithoutValidation("X-Echo", echo);
            return Task.FromResult(response);
        });

        var error = await rig.SendAsync(Scenarios.Minimal() with { HtmlBody = $"<p>{Scenarios.Body}</p>", Cc = ["cc.SENTINEL@example.test"] });
        var health = await rig.Provider.CheckAsync(CancellationToken.None);

        Assert.NotNull(error);
        rig.AssertNoLeaks(error.Message, health.Message);
    }

    // ACS keeps its own provider (SDK path) but declares the same capabilities.
    private static ProviderCapabilities Capabilities(ConformanceRig rig) => rig.Provider switch
    {
        PipelineEmailProvider pipeline => pipeline.Mapper.Capabilities,
        Umbraco.Community.EmailSender.Providers.AzureCommunicationServices.AcsProvider => Umbraco.Community.EmailSender.Providers.AzureCommunicationServices.AcsProvider.Capabilities,
        var other => throw new InvalidOperationException(other.GetType().Name),
    };
}
