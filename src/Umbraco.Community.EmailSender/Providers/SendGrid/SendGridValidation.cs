using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.SendGrid;

internal static class SendGridValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        ProviderValidation.Secret(failures, EmailProviderKind.SendGrid, nameof(SendGridOptions.ApiKey), options.SendGrid.ApiKey);
        ProviderValidation.Region(failures, EmailProviderKind.SendGrid, options.SendGrid.Region, SendGridMapper.Regions);
    }
}
