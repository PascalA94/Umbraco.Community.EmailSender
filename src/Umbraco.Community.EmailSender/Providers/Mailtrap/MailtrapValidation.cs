using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Mailtrap;

internal static class MailtrapValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.Mailtrap, nameof(MailtrapOptions.ApiKey), options.Mailtrap.ApiKey);
}
