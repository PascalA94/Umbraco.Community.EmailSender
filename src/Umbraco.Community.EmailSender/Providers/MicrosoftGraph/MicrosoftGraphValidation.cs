using System.Text.RegularExpressions;
using MimeKit;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

/// <summary>
/// Start-up checks for Graph (design 9.3). The tenant and the mailbox become URL path segments, so they are checked again at
/// send time: a reloaded configuration is not validated, and a value such as <c>..</c> would change the path.
/// </summary>
internal static partial class MicrosoftGraphValidation
{
    internal const string TenantMessage = "EmailSender:MicrosoftGraph:TenantId must be a GUID or a domain name.";
    internal const string SenderMessage = "EmailSender:MicrosoftGraph:SenderUserId must be a GUID or an email address.";

    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        var graph = options.MicrosoftGraph;
        ProviderValidation.Secret(failures, EmailProviderKind.MicrosoftGraph, nameof(MicrosoftGraphOptions.ClientSecret), graph.ClientSecret);
        Required(failures, nameof(MicrosoftGraphOptions.TenantId), graph.TenantId, IsTenant, TenantMessage);
        Required(failures, nameof(MicrosoftGraphOptions.ClientId), graph.ClientId, IsGuid, "EmailSender:MicrosoftGraph:ClientId must be a GUID.");
        Required(failures, nameof(MicrosoftGraphOptions.SenderUserId), graph.SenderUserId, IsSender, SenderMessage);
    }

    private static void Required(List<string> failures, string key, string? value, Func<string, bool> valid, string invalid)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"EmailSender:MicrosoftGraph:{key} is required when EmailSender:Provider is MicrosoftGraph.");
        }
        else if (!valid(value))
        {
            failures.Add(invalid);
        }
    }

    /// <summary>The plain <c>D</c> format only: no braces, no spaces, so the value is safe in a path.</summary>
    internal static bool IsGuid(string value)
        // TryParseExact tolerates surrounding whitespace, and the raw value becomes a path segment: only the canonical text passes.
        => Guid.TryParseExact(value, "D", out var guid) && string.Equals(value, guid.ToString("D"), StringComparison.OrdinalIgnoreCase);

    internal static bool IsTenant(string value) => IsGuid(value) || HostPattern().IsMatch(value);

    /// <summary>A GUID, or a bare address with no display name, comment or list syntax.</summary>
    internal static bool IsSender(string value)
        => IsGuid(value)
           || (AddressPattern().IsMatch(value) && MailboxAddress.TryParse(value, out var mailbox) && mailbox.Address == value);

    // \z, not $: $ allows a trailing newline.
    [GeneratedRegex(@"^(?=.{1,253}\z)[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)+\z")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._+\-]{1,64}@[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)+\z")]
    private static partial Regex AddressPattern();
}
