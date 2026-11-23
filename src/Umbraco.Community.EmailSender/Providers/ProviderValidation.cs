using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers;

/// <summary>Start-up checks every provider shares (design 9.3). Failures name the config key, never the value.</summary>
internal static class ProviderValidation
{
    /// <summary>A secret is required, and a control or non-ASCII character (a trailing newline from a mounted secret) would make every send fail.</summary>
    public static bool Secret(List<string> failures, EmailProviderKind kind, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"EmailSender:{kind}:{key} is required when EmailSender:Provider is {kind}.");
            return false;
        }

        if (value.Any(c => char.IsControl(c) || c > 126))
        {
            failures.Add($"EmailSender:{kind}:{key} contains a control or non-ASCII character (a trailing newline?).");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The canonical spelling of <paramref name="value"/> among <paramref name="allowed"/> (case-insensitive), or null. Regions are
    /// strings, not enums, so an unknown value is a named failure here rather than a binder error that echoes the value.
    /// </summary>
    public static string? MatchRegion(string? value, IReadOnlyList<string> allowed)
        => allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static void Region(List<string> failures, EmailProviderKind kind, string? value, IReadOnlyList<string> allowed)
    {
        if (MatchRegion(value, allowed) is null)
        {
            failures.Add($"EmailSender:{kind}:Region must be one of: {string.Join(", ", allowed)}.");
        }
    }
}
