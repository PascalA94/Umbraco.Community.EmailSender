namespace Umbraco.Community.EmailSender.Configuration;

/// <summary>The providers selectable through <c>EmailSender:Provider</c>. Names are permanent config values.</summary>
public enum EmailProviderKind
{
    AzureCommunicationServices,
    Postmark,
    GovUkNotify,
}

/// <summary>Parses <c>EmailSender:Provider</c> case-insensitively and names the allowed values on failure.</summary>
public static class EmailProviderKindParser
{
    public static bool TryParse(string? value, out EmailProviderKind kind)
    {
        // Enum.TryParse alone also accepts "0" and "A,B"; only an exact name may select a provider.
        var name = Enum.GetNames<EmailProviderKind>()
            .FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        kind = name is null ? default : Enum.Parse<EmailProviderKind>(name);
        return name is not null;
    }

    /// <exception cref="InvalidOperationException">The value is not a known provider. The message never echoes the value.</exception>
    public static EmailProviderKind Parse(string? value)
    {
        if (TryParse(value, out var kind))
        {
            return kind;
        }

        // The configured value is deliberately not echoed: a mistyped key could land in this setting.
        throw new InvalidOperationException(
            $"EmailSender:Provider is not a known provider. Allowed values: {string.Join(", ", Enum.GetNames<EmailProviderKind>())}.");
    }
}
