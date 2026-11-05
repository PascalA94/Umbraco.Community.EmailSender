using Microsoft.Extensions.Options;

namespace Umbraco.Community.EmailSender.Configuration;

/// <summary>Start-up validation. Failures name the config key, never a value.</summary>
public sealed class EmailSenderOptionsValidator : IValidateOptions<EmailSenderOptions>
{
    // Attachments are buffered in memory and base64-encoded, and the providers refuse far less than this anyway.
    internal const long MaxAttachmentBytesLimit = 50 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, EmailSenderOptions options)
    {
        if (options.Provider is null)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add("EmailSender:TimeoutSeconds must be greater than zero.");
        }

        if (options.MaxTotalAttachmentBytes is <= 0 or > MaxAttachmentBytesLimit)
        {
            failures.Add($"EmailSender:MaxTotalAttachmentBytes must be between 1 and {MaxAttachmentBytesLimit}.");
        }

        switch (options.Provider)
        {
            case EmailProviderKind.Postmark when string.IsNullOrWhiteSpace(options.Postmark.ServerToken):
                failures.Add("EmailSender:Postmark:ServerToken is required when EmailSender:Provider is Postmark.");
                break;
            case EmailProviderKind.Postmark when string.IsNullOrWhiteSpace(options.Postmark.MessageStream):
                failures.Add("EmailSender:Postmark:MessageStream must not be blank.");
                break;
            case EmailProviderKind.AzureCommunicationServices
                when string.IsNullOrWhiteSpace(options.AzureCommunicationServices.ConnectionString):
                failures.Add("EmailSender:AzureCommunicationServices:ConnectionString is required when EmailSender:Provider is AzureCommunicationServices.");
                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
