using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Bird;

internal static class BirdValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        ProviderValidation.Secret(failures, EmailProviderKind.Bird, nameof(BirdOptions.ApiKey), options.Bird.ApiKey);
        ProviderValidation.Region(failures, EmailProviderKind.Bird, options.Bird.Region, BirdMapper.Regions);
    }
}
