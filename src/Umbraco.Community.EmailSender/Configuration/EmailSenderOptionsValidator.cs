using Microsoft.Extensions.Options;

namespace Umbraco.Community.EmailSender.Configuration;

/// <summary>Start-up validation. Failures name the config key, never a value.</summary>
public sealed class EmailSenderOptionsValidator : IValidateOptions<EmailSenderOptions>
{
    // Attachments are buffered in memory and base64-encoded, and the providers refuse far less than this anyway.
    internal const int MaxTimeoutSeconds = 300;
    internal const long MaxAttachmentBytesLimit = 50 * 1024 * 1024;

    private static void ValidateNotify(NotifyOptions notify, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(notify.ApiKey))
        {
            failures.Add("EmailSender:GovUkNotify:ApiKey is required when EmailSender:Provider is GovUkNotify.");
        }
        else if (!Providers.GovUkNotify.NotifyApiKey.TryParse(notify.ApiKey, out _, out _))
        {
            // The value is never echoed: a mistyped key still looks like a secret. A trailing newline is the usual cause.
            failures.Add("EmailSender:GovUkNotify:ApiKey is not a GOV.UK Notify API key (a trailing newline or a truncated key?).");
        }

        if (!Guid.TryParseExact(notify.EmailTemplateId?.Trim(), "D", out _))
        {
            failures.Add("EmailSender:GovUkNotify:EmailTemplateId must be the template's id (a GUID).");
        }

        if (!string.IsNullOrWhiteSpace(notify.ReplyToId) && !Guid.TryParseExact(notify.ReplyToId.Trim(), "D", out _))
        {
            failures.Add("EmailSender:GovUkNotify:ReplyToId must be a reply-to address id (a GUID) or left empty.");
        }
    }

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
            // Only the shape is checked here and the value is never echoed; a typo would otherwise surface as a failed send.
            case EmailProviderKind.AzureCommunicationServices
                when !Providers.AzureCommunicationServices.AcsProvider.TryParse(options.AzureCommunicationServices.ConnectionString, out _, out _):
                failures.Add("EmailSender:AzureCommunicationServices:ConnectionString is not a valid Azure Communication Services connection string (an https endpoint on communication.azure.com, .us or .cn, and a base64 accesskey).");
                break;
            case EmailProviderKind.GovUkNotify:
                ValidateNotify(options.GovUkNotify, failures);
                break;
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
