using System.Net;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>The primary handler under every named client in a conformance rig: records each request and answers by call number.</summary>
internal sealed class FakeProviderServer(
    TimeProvider clock,
    Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
    Func<HttpRequestMessage, HttpResponseMessage?>? auxiliary = null) : HttpMessageHandler
{
    private int inFlight;

    /// <summary>Requests the profile's auxiliary answer took (Graph's token requests): not counted as sends.</summary>
    public List<(HttpRequestMessage Request, byte[] Body)> AuxCalls { get; } = [];

    public List<(HttpRequestMessage Request, byte[] Body, DateTimeOffset At)> Calls { get; } = [];

    /// <summary>Requests the server is still holding open (a hang scenario).</summary>
    public int InFlight => Volatile.Read(ref inFlight);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        if (auxiliary?.Invoke(request) is { } aux)
        {
            AuxCalls.Add((request, body));
            return aux;
        }

        Calls.Add((request, body, clock.GetUtcNow()));
        var answer = respond(Calls.Count, request, ct);
        // Only an answer that is genuinely still pending is a hang. Counting an instant one made the clock driver, sampling
        // mid-answer, advance past the send budget and time out a healthy send (seen once under load in M8).
        if (answer.IsCompleted)
        {
            return await answer;
        }

        Interlocked.Increment(ref inFlight);
        try
        {
            return await answer;
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    // The rig's service provider would dispose the handler with the factory; the test owns it.
    protected override void Dispose(bool disposing)
    {
    }

    /// <summary>The profile's success response, headers included.</summary>
    public static HttpResponseMessage Success(IProviderProfile profile)
    {
        var body = profile.Expect.SuccessBody.Length == 0 || profile.Expect.SuccessBody.TrimStart() is ['{' or '[', ..]
            ? profile.Expect.SuccessBody
            : FixtureLoader.Text(profile.FixtureFolder!, profile.Expect.SuccessBody);
        var response = Answer(profile.Expect.SuccessStatus, body);
        foreach (var (name, value) in profile.Expect.SuccessHeaders)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public static HttpResponseMessage Answer(HttpStatusCode status, string body, string? retryAfter = null)
    {
        // Non-seekable, like a socket: the ACS SDK takes a different (and failing) path for a seekable body.
        var response = new HttpResponseMessage(status) { Content = new StreamContent(new NetworkLikeStream(System.Text.Encoding.UTF8.GetBytes(body))) };
        response.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }
}

/// <summary>A response body that counts how much of it was read, for the 64 KB cap (F14).</summary>
internal sealed class CountingStream(long length) : Stream
{
    public long Consumed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => Consumed; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = (int)Math.Min(count, length - Consumed);
        Array.Fill(buffer, (byte)'x', offset, n);
        Consumed += n;
        return n;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A response body that cannot seek, as a real network stream.</summary>
internal sealed class NetworkLikeStream(byte[] bytes) : Stream
{
    private readonly MemoryStream inner = new(bytes);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
