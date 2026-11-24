using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Smtp2Go;

internal static class Smtp2GoValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        ProviderValidation.Secret(failures, EmailProviderKind.Smtp2Go, nameof(Smtp2GoOptions.ApiKey), options.Smtp2Go.ApiKey);
        ProviderValidation.Region(failures, EmailProviderKind.Smtp2Go, options.Smtp2Go.Region, Smtp2GoMapper.Regions.Keys.ToList());
    }
}
