using System.Text.RegularExpressions;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Mailgun;

internal static partial class MailgunValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        var mailgun = options.Mailgun;
        ProviderValidation.Secret(failures, EmailProviderKind.Mailgun, nameof(MailgunOptions.ApiKey), mailgun.ApiKey);
        ProviderValidation.Region(failures, EmailProviderKind.Mailgun, mailgun.Region, MailgunMapper.Regions);
        if (string.IsNullOrWhiteSpace(mailgun.Domain))
        {
            failures.Add("EmailSender:Mailgun:Domain is required when EmailSender:Provider is Mailgun.");
        }
        else if (!IsBareHost(mailgun.Domain))
        {
            failures.Add(DomainMessage);
        }
    }

    internal const string DomainMessage = "EmailSender:Mailgun:Domain must be a bare host name (no scheme, slash or port).";

    /// <summary>Letters, digits and hyphens in dot-separated labels, at least two labels. The value becomes a URL path segment, so nothing else may pass.</summary>
    internal static bool IsBareHost(string value) => HostPattern().IsMatch(value);

    // \z, not $: $ allows a trailing newline.
    [GeneratedRegex(@"^(?=.{1,253}\z)[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)+\z")]
    private static partial Regex HostPattern();
}
