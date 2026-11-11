using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Configuration;

/// <summary>Bound from the <c>EmailSender</c> configuration section. Secrets never appear in <c>ToString()</c> or JSON.</summary>
public sealed class EmailSenderOptions
{
    public const string SectionName = "EmailSender";

    /// <summary>Unset means the package does nothing and core SMTP stays in place.</summary>
    public EmailProviderKind? Provider { get; set; }

    /// <summary>Used when a message has no From. Falls back to <c>Umbraco:CMS:Global:Smtp:From</c>.</summary>
    public string? From { get; set; }

    public bool DisableLinkTracking { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public long MaxTotalAttachmentBytes { get; set; } = 10485760;

    public AcsOptions AzureCommunicationServices { get; set; } = new();

    public PostmarkOptions Postmark { get; set; } = new();

    public NotifyOptions GovUkNotify { get; set; } = new();

    public override string ToString() => $"EmailSenderOptions {{ Provider = {Provider}, Secrets = *** }}";
}

public sealed class AcsOptions
{
    [JsonIgnore]
    public string? ConnectionString { get; set; }

    public override string ToString() => "AcsOptions { ConnectionString = *** }";
}

public sealed class PostmarkOptions
{
    [JsonIgnore]
    public string? ServerToken { get; set; }

    public string MessageStream { get; set; } = "outbound";

    public override string ToString() => $"PostmarkOptions {{ ServerToken = ***, MessageStream = {MessageStream} }}";
}

public sealed class NotifyOptions
{
    /// <summary>The full Notify API key (name, service id and secret). Never logged.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>One email template whose subject is <c>((subject))</c> and whose body is <c>((body))</c>.</summary>
    public string? EmailTemplateId { get; set; }

    /// <summary>Optional id of a reply-to address registered in Notify. Notify cannot take a free-form reply-to.</summary>
    public string? ReplyToId { get; set; }

    public override string ToString() => $"NotifyOptions {{ ApiKey = ***, EmailTemplateId = {EmailTemplateId} }}";
}
