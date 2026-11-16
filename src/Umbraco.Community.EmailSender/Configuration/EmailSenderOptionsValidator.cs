using Microsoft.Extensions.Options;
using Umbraco.Community.EmailSender.Providers.Registry;

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

        // Each provider brings its own checks (design 9.13 row 9); the messages are unchanged from the switch they replaced.
        ProviderRegistry.Get(options.Provider.Value).Validate(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
