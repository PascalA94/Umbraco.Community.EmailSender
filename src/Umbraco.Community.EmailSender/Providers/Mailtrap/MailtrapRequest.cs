using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Mailtrap;

/// <summary>The body of <c>POST /api/send</c>. Null members are left out so Mailtrap's own defaults apply.</summary>
internal sealed record MailtrapRequest(
    [property: JsonPropertyName("from")] MailtrapAddress From,
    [property: JsonPropertyName("to")] IReadOnlyList<MailtrapAddress> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<MailtrapAddress>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<MailtrapAddress>? Bcc,
    [property: JsonPropertyName("reply_to")] MailtrapAddress? ReplyTo,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("attachments")] IReadOnlyList<MailtrapAttachment>? Attachments)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "MailtrapRequest";
}

internal sealed record MailtrapAddress(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public override string ToString() => "MailtrapAddress";
}

/// <summary>Always <c>disposition: attachment</c>: Umbraco's message has no content id, so nothing is sent inline.</summary>
internal sealed record MailtrapAttachment(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("disposition")] string Disposition)
{
    public override string ToString() => "MailtrapAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MailtrapRequest))]
internal sealed partial class MailtrapJsonContext : JsonSerializerContext;
