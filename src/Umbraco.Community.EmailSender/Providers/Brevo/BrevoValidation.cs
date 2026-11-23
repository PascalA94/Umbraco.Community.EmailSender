using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Brevo;

internal static class BrevoValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.Brevo, nameof(BrevoOptions.ApiKey), options.Brevo.ApiKey);
}
