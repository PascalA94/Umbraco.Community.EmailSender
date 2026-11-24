using System.Text.Json;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

/// <summary>
/// The Entra token response. The token is opaque (never parsed as a JWT) and never printed: <see cref="ToString"/> hides it.
/// </summary>
internal sealed partial record GraphTokenResponse(string AccessToken, int ExpiresInSeconds)
{
    public override string ToString() => $"GraphTokenResponse {{ AccessToken = ***, ExpiresInSeconds = {ExpiresInSeconds} }}";

    /// <summary>Null unless the body has a non-empty <c>access_token</c> and a positive <c>expires_in</c>.</summary>
    public static GraphTokenResponse? TryParse(ReadOnlyMemory<byte> body)
    {
        using var json = ResponseReader.TryParse(body);
        if (json is null)
        {
            return null;
        }

        var token = ResponseReader.String(json.RootElement, "access_token");
        var expires = ResponseReader.Int(json.RootElement, "expires_in");
        // RFC 6750 token characters only: anything else cannot go in an Authorization header, and caching it would fail every send until it expired.
        return token is null || !TokenPattern().IsMatch(token) || expires is not > 0 ? null : new GraphTokenResponse(token, expires.Value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9\-._~+/]+=*\z")]
    private static partial System.Text.RegularExpressions.Regex TokenPattern();
}
