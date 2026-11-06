namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

/// <summary>
/// A Notify API key is <c>{name}-{service id}-{secret}</c>, where the last two parts are 36-character GUIDs. The service id
/// is the JWT issuer and the secret signs it.
/// </summary>
internal static class NotifyApiKey
{
    private const int GuidLength = 36;

    public static bool TryParse(string? key, out string serviceId, out string secret)
    {
        serviceId = secret = string.Empty;
        // Control and non-ASCII characters are what a trailing newline or a pasted smart quote leave behind.
        if (key is null || key.Length < (2 * GuidLength) + 1 || key.Any(c => char.IsControl(c) || c > 126))
        {
            return false;
        }

        var secretPart = key[^GuidLength..];
        var servicePart = key[^((2 * GuidLength) + 1)..^(GuidLength + 1)];
        if (key[^(GuidLength + 1)] != '-'
            || (key.Length > (2 * GuidLength) + 1 && key[^((2 * GuidLength) + 2)] != '-')
            || !Guid.TryParseExact(servicePart, "D", out _)
            || !Guid.TryParseExact(secretPart, "D", out _))
        {
            return false;
        }

        serviceId = servicePart;
        secret = secretPart;
        return true;
    }
}
