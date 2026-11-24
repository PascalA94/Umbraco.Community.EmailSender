namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>A provider-neutral view of one recorded request, built by the profile.</summary>
internal sealed record RecordedEmail
{
    public string? FromAddress { get; init; }

    public string? FromName { get; init; }

    public IReadOnlyList<string> To { get; init; } = [];

    public IReadOnlyList<string> Cc { get; init; } = [];

    public IReadOnlyList<string> Bcc { get; init; } = [];

    public IReadOnlyList<string> ReplyTo { get; init; } = [];

    public string? Subject { get; init; }

    public string? Html { get; init; }

    public string? Text { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<RecordedAttachment> Attachments { get; init; } = [];

    /// <summary>True when the request switches tracking off; null when it says nothing about tracking.</summary>
    public bool? TrackingOff { get; init; }

    public string? IdempotencyKey { get; init; }

    /// <summary>The form part names, for a multipart request (Mailgun): the shape match compares these with the curl fixtures.</summary>
    public IReadOnlyList<string>? PartNames { get; init; }

    /// <summary>The request body as JSON for the shape match, or null when the body is not JSON.</summary>
    public System.Text.Json.JsonElement? Json { get; init; }
}

internal sealed record RecordedAttachment(string Name, string ContentType, string Base64, bool Inline = false);
