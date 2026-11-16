namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>What custom headers a provider takes (design 9.2, "Custom headers").</summary>
internal enum HeaderPolicy
{
    SendAsGiven,
    /// <summary>The provider refuses or bills for custom headers (Brevo, MailerSend): none is sent.</summary>
    DropAll,
    /// <summary>Only names starting with <c>x-</c> (Microsoft Graph).</summary>
    XPrefixedOnly,
}

internal enum ReplyToStyle
{
    List,
    /// <summary>One address only: the pipeline keeps the first and warns with the count dropped.</summary>
    Single,
    CommaSeparated,
    CustomHeader,
    FormPart,
    /// <summary>No per-message Reply-To (GOV.UK Notify): ignored with a warning, never a failure.</summary>
    NotSupported,
}

internal enum TrackingStyle
{
    PerRequest,
    /// <summary>Set on the account, domain or key; the README says where.</summary>
    AccountLevel,
    NotApplicable,
}

internal enum IdempotencyCarriage
{
    None,
    Header,
    Body,
}

internal enum RecipientPolicy
{
    /// <summary>One request carrying To, Cc and Bcc.</summary>
    Batch,
    /// <summary>One request per To address (GOV.UK Notify).</summary>
    PerRecipient,
}

/// <summary>How a provider could address an inline image. Recorded for a future inline feature; nothing is sent inline today.</summary>
internal enum InlineContentId
{
    None,
    FileNameOnly,
    ContentId,
}

/// <summary>One provider's limits and quirks. The pipeline enforces the caps before any request (design 9.2 step 1).</summary>
internal sealed record ProviderCapabilities
{
    /// <summary>To + Cc + Bcc for a batch send; To alone for a per-recipient send. 50 for every provider.</summary>
    public int MaxRecipients { get; init; } = 50;

    public int? MaxCc { get; init; }

    public int? MaxBcc { get; init; }

    public int? MaxAttachments { get; init; }

    /// <summary>The whole serialised request, after base64. Null: the package cap only.</summary>
    public long? MaxRequestBytes { get; init; }

    /// <summary>Raw bytes per attachment, before encoding.</summary>
    public long? MaxAttachmentBytesEach { get; init; }

    public HeaderPolicy Headers { get; init; }

    public ReplyToStyle ReplyTo { get; init; }

    /// <summary>Fixed text added to the warning when a Reply-To is ignored, for example the setting to use instead.</summary>
    public string? ReplyToHint { get; init; }

    public TrackingStyle Tracking { get; init; }

    public IdempotencyCarriage Idempotency { get; init; }

    public RecipientPolicy Recipients { get; init; }

    public InlineContentId Inline { get; init; }

    /// <summary>The provider documents failures inside a 2xx, so a 2xx body that cannot be read is a failure (design 9.2).</summary>
    public bool FailClosedOn2xx { get; init; }

    /// <summary>A word printed before the provider's code in error text, for example Postmark's "ErrorCode". Null: the bare code.</summary>
    public string? ErrorCodeLabel { get; init; }
}
