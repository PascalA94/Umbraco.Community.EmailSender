using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Mandrill;

internal static class MandrillValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.Mandrill, nameof(MandrillOptions.ApiKey), options.Mandrill.ApiKey);
}
