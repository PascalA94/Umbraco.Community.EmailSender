namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>
/// Design 9.2 step 1: caps checked before any request, so nothing is sent and the editor gets a clear reason. ACS calls this
/// too, before its SDK. The request size is checked later, once the body is serialised (<see cref="ProviderPipeline"/>).
/// </summary>
internal static class ProviderPreChecks
{
    /// <exception cref="EmailSendException">A cap is exceeded. The text names the limit, never an address or file name.</exception>
    public static void Run(string name, OutboundEmail email, ProviderCapabilities caps)
    {
        if (caps.MaxAttachments == 0 && email.Attachments.Count > 0)
        {
            throw new EmailSendException($"{name} does not support attachments. Remove them or use another provider.");
        }

        if (caps.MaxCc == 0 && caps.MaxBcc == 0 && email.Cc.Count + email.Bcc.Count > 0)
        {
            throw new EmailSendException($"{name} does not support Cc or Bcc. Send to the recipients in To instead.");
        }

        if (caps.Recipients == RecipientPolicy.PerRecipient)
        {
            if (email.To.Count > caps.MaxRecipients)
            {
                throw new EmailSendException($"{name} accepts at most {caps.MaxRecipients} recipients in To (one request each).");
            }
        }
        else if (email.To.Count + email.Cc.Count + email.Bcc.Count > caps.MaxRecipients)
        {
            throw new EmailSendException($"{name} accepts at most {caps.MaxRecipients} recipients in To, Cc and Bcc together.");
        }

        if (email.Cc.Count > caps.MaxCc)
        {
            throw new EmailSendException($"{name} accepts at most {caps.MaxCc} recipients in Cc.");
        }

        if (email.Bcc.Count > caps.MaxBcc)
        {
            throw new EmailSendException($"{name} accepts at most {caps.MaxBcc} recipients in Bcc.");
        }

        if (email.Attachments.Count > caps.MaxAttachments)
        {
            throw new EmailSendException($"{name} accepts at most {caps.MaxAttachments} attachments.");
        }

        if (caps.MaxAttachmentBytesEach is { } each && email.Attachments.Any(a => RawLength(a.Base64) > each))
        {
            throw new EmailSendException($"{name} accepts attachments up to {FormatSize(each)} each.");
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 when bytes % (1024 * 1024) == 0 => $"{bytes / (1024 * 1024)} MB",
        >= 1024 when bytes % 1024 == 0 => $"{bytes / 1024} KB",
        _ => $"{bytes} bytes",
    };

    private static long RawLength(string base64)
    {
        var padding = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        return (long)base64.Length / 4 * 3 - padding;
    }
}
