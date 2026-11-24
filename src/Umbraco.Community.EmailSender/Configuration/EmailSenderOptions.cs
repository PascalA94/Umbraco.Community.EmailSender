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

    public SendGridOptions SendGrid { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();

    public MailgunOptions Mailgun { get; set; } = new();

    public BrevoOptions Brevo { get; set; } = new();

    public MailjetOptions Mailjet { get; set; } = new();

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

public sealed class SendGridOptions
{
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary><c>Global</c> (default) or <c>EU</c>. EU needs a Pro or Premier plan and an EU subuser's key.</summary>
    public string Region { get; set; } = "Global";

    public override string ToString() => $"SendGridOptions {{ ApiKey = ***, Region = {Region} }}";
}

public sealed class ResendOptions
{
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "ResendOptions { ApiKey = *** }";
}

public sealed class MailgunOptions
{
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>The sending domain, a bare host name such as <c>mg.example.com</c>. It becomes a URL path segment.</summary>
    public string? Domain { get; set; }

    /// <summary><c>US</c> (default) or <c>EU</c>. Must match the region the domain was created in.</summary>
    public string Region { get; set; } = "US";

    public override string ToString() => $"MailgunOptions {{ ApiKey = ***, Domain = {Domain}, Region = {Region} }}";
}

public sealed class BrevoOptions
{
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "BrevoOptions { ApiKey = *** }";
}

public sealed class MailjetOptions
{
    /// <summary>The public API key. It is the Basic username, and still treated as a secret in logs.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>The private (secret) API key.</summary>
    [JsonIgnore]
    public string? SecretKey { get; set; }

    public override string ToString() => "MailjetOptions { ApiKey = ***, SecretKey = *** }";
}
