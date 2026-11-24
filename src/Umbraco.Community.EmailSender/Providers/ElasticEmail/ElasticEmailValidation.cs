using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.ElasticEmail;

internal static class ElasticEmailValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
        => ProviderValidation.Secret(failures, EmailProviderKind.ElasticEmail, nameof(ElasticEmailOptions.ApiKey), options.ElasticEmail.ApiKey);
}
