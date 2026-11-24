using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Resend;

internal static class ResendValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.Resend, nameof(ResendOptions.ApiKey), options.Resend.ApiKey);
}
