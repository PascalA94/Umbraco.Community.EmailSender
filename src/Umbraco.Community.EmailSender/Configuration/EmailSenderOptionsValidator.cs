using Microsoft.Extensions.Options;

namespace Umbraco.Community.EmailSender.Configuration;

/// <summary>Start-up validation. Failures name the config key, never a value.</summary>
public sealed class EmailSenderOptionsValidator : IValidateOptions<EmailSenderOptions>
{
    // Attachments are buffered in memory and base64-encoded, and the providers refuse far less than this anyway.
    internal const int MaxTimeoutSeconds = 300;
    internal const long MaxAttachmentBytesLimit = 50 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, EmailSenderOptions options)
    {
        if (options.Provider is null)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // The provider enforces this as its only bound (HttpClient.Timeout is infinite), and a huge value overflows the
        // timer, so it is capped.
        if (options.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            failures.Add($"EmailSender:TimeoutSeconds must be between 1 and {MaxTimeoutSeconds}.");
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
            // A trailing newline from a file-mounted secret would make every send fail with a bare FormatException.
            case EmailProviderKind.Postmark when options.Postmark.ServerToken!.Any(c => char.IsControl(c) || c > 126):
                failures.Add("EmailSender:Postmark:ServerToken contains a control or non-ASCII character (a trailing newline?).");
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
