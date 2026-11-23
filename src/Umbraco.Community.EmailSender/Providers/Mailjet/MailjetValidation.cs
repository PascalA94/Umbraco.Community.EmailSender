using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Mailjet;

internal static class MailjetValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        ProviderValidation.Secret(failures, EmailProviderKind.Mailjet, nameof(MailjetOptions.ApiKey), options.Mailjet.ApiKey);
        ProviderValidation.Secret(failures, EmailProviderKind.Mailjet, nameof(MailjetOptions.SecretKey), options.Mailjet.SecretKey);
    }
}
