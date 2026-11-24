using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.MailerSend;

internal static class MailerSendValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.MailerSend, nameof(MailerSendOptions.ApiKey), options.MailerSend.ApiKey);
}
