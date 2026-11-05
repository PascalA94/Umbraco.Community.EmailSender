using System.Net;
using System.Text;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>Records every request (with its body) and answers through a delegate.</summary>
internal sealed class Recorder(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct)));
        return await respond(request, ct);
    }
}
