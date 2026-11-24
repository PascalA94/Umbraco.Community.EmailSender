using System.Net;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>
/// The clock driver reads <see cref="FakeProviderServer.InFlight"/> as "a request is hanging, so only the budget can end it".
/// A responder that answers at once must never count, or a sample taken mid-answer advances the clock past the send budget and
/// fails a healthy send with a timeout (seen once under load in M8).
/// </summary>
public class FakeProviderServerTests
{
    [Fact]
    public async Task InstantResponder_IsNeverCountedAsInFlight()
    {
        FakeProviderServer? server = null;
        var seen = -1;
        server = new FakeProviderServer(TimeProvider.System, (_, _, _) =>
        {
            seen = server!.InFlight;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var invoker = new HttpMessageInvoker(server);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://example.test/"), CancellationToken.None);

        Assert.Equal(0, seen);
        Assert.Equal(0, server.InFlight);
    }

    [Fact]
    public async Task HangingResponder_IsCountedUntilItEnds()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>();
        using var server = new FakeProviderServer(TimeProvider.System, (_, _, _) => release.Task);
        using var invoker = new HttpMessageInvoker(server);

        var send = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://example.test/"), CancellationToken.None);
        while (server.InFlight == 0)
        {
            await Task.Delay(1);
        }

        Assert.Equal(1, server.InFlight);
        release.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        (await send).Dispose();
        Assert.Equal(0, server.InFlight);
    }
}
