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
    public BirdOptions Bird { get; set; } = new();
    public MailerSendOptions MailerSend { get; set; } = new();
    public Smtp2GoOptions Smtp2Go { get; set; } = new();
    public ElasticEmailOptions ElasticEmail { get; set; } = new();
    public MandrillOptions Mandrill { get; set; } = new();
    public MailtrapOptions Mailtrap { get; set; } = new();
    public MicrosoftGraphOptions MicrosoftGraph { get; set; } = new();

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

public sealed class BirdOptions
{
    /// <summary>A workspace key (<c>bk_&lt;region&gt;_...</c>) or an organisation key (<c>bm_&lt;region&gt;_...</c>).</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary><c>us1</c> (default) or <c>eu1</c>. Fixed per organisation at sign-up; the key must match.</summary>
    public string Region { get; set; } = "us1";

    public override string ToString() => $"BirdOptions {{ ApiKey = ***, Region = {Region} }}";
}

public sealed class MailerSendOptions
{
    /// <summary>The API token. Tokens are per sending domain, with scoped permissions.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "MailerSendOptions { ApiKey = *** }";
}

public sealed class Smtp2GoOptions
{
    /// <summary>The API key (<c>api-...</c>). Open and click tracking are settings on the key.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary><c>Global</c> (default, nearest region by resolver location), <c>US</c>, <c>EU</c> or <c>AU</c>. Use a regional value for data residency.</summary>
    public string Region { get; set; } = "Global";

    public override string ToString() => $"Smtp2GoOptions {{ ApiKey = ***, Region = {Region} }}";
}

public sealed class ElasticEmailOptions
{
    /// <summary>The API key. It needs the <c>SendHttp</c> access level.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "ElasticEmailOptions { ApiKey = *** }";
}

public sealed class MandrillOptions
{
    /// <summary>The Mandrill (Mailchimp Transactional) API key. It travels in the request body, never in a header.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "MandrillOptions { ApiKey = *** }";
}

public sealed class MailtrapOptions
{
    /// <summary>The Email Sending API token (not a Sandbox token), scoped to a verified sending domain.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    public override string ToString() => "MailtrapOptions { ApiKey = *** }";
}

public sealed class MicrosoftGraphOptions
{
    /// <summary>The Entra tenant: a GUID or a domain name such as <c>contoso.onmicrosoft.com</c>. It becomes a URL path segment.</summary>
    public string? TenantId { get; set; }

    /// <summary>The application (client) id of the Entra app registration, a GUID.</summary>
    public string? ClientId { get; set; }

    /// <summary>The app's client secret. Certificate credentials are not supported yet.</summary>
    [JsonIgnore]
    public string? ClientSecret { get; set; }

    /// <summary>The mailbox to send as: its object id (a GUID) or its address. It becomes a URL path segment.</summary>
    public string? SenderUserId { get; set; }

    /// <summary>Keep a copy in the mailbox's Sent Items. Off by default: invites and resets would clutter it.</summary>
    public bool SaveToSentItems { get; set; }

    public override string ToString() => $"MicrosoftGraphOptions {{ ClientSecret = ***, SaveToSentItems = {SaveToSentItems} }}";
}
