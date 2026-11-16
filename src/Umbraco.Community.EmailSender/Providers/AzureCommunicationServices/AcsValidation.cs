using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;

internal static class AcsValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        var connectionString = options.AzureCommunicationServices.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            failures.Add("EmailSender:AzureCommunicationServices:ConnectionString is required when EmailSender:Provider is AzureCommunicationServices.");
        }
        // Only the shape is checked here and the value is never echoed; a typo would otherwise surface as a failed send.
        else if (!AcsProvider.TryParse(connectionString, out _, out _))
        {
            failures.Add("EmailSender:AzureCommunicationServices:ConnectionString is not a valid Azure Communication Services connection string (an https endpoint on communication.azure.com, .us or .cn, and a base64 accesskey).");
        }
    }
}
