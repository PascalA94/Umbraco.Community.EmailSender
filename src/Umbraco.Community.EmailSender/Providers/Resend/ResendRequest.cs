using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Resend;

/// <summary>The body of <c>POST /emails</c>. Null members are left out so Resend's own defaults apply.</summary>
internal sealed record ResendRequest(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] IReadOnlyList<string> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<string>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<string>? Bcc,
    [property: JsonPropertyName("reply_to")] IReadOnlyList<string>? ReplyTo,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("attachments")] IReadOnlyList<ResendAttachment>? Attachments)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "ResendRequest";
}

internal sealed record ResendAttachment(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("content_type")] string ContentType)
{
    public override string ToString() => "ResendAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResendRequest))]
internal sealed partial class ResendJsonContext : JsonSerializerContext;
