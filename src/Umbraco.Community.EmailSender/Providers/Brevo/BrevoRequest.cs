using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Brevo;

/// <summary>The body of <c>POST /v3/smtp/email</c>. Null members are left out.</summary>
internal sealed record BrevoRequest(
    [property: JsonPropertyName("sender")] BrevoAddress Sender,
    [property: JsonPropertyName("to")] IReadOnlyList<BrevoAddress> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<BrevoAddress>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<BrevoAddress>? Bcc,
    [property: JsonPropertyName("replyTo")] BrevoAddress? ReplyTo,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("htmlContent")] string? HtmlContent,
    [property: JsonPropertyName("textContent")] string? TextContent,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
    [property: JsonPropertyName("attachment")] IReadOnlyList<BrevoAttachment>? Attachment)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "BrevoRequest";
}

internal sealed record BrevoAddress(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public override string ToString() => "BrevoAddress";
}

internal sealed record BrevoAttachment(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("content")] string Content)
{
    public override string ToString() => "BrevoAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BrevoRequest))]
internal sealed partial class BrevoJsonContext : JsonSerializerContext;
