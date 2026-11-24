using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Bird;

/// <summary>
/// The body of <c>POST /v1/email/messages</c>. Bird's schema rejects unknown fields, so nothing but these is sent. Null members
/// are left out so Bird's own defaults apply.
/// </summary>
internal sealed record BirdRequest(
    [property: JsonPropertyName("from")] BirdAddress From,
    [property: JsonPropertyName("to")] IReadOnlyList<string> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<string>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<string>? Bcc,
    [property: JsonPropertyName("reply_to")] IReadOnlyList<string>? ReplyTo,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("attachments")] IReadOnlyList<BirdAttachment>? Attachments,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("track_opens")] bool? TrackOpens,
    [property: JsonPropertyName("track_clicks")] bool? TrackClicks)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "BirdRequest";
}

/// <summary>The object form of <c>from</c>, as in Bird's own basic example; a display name never has to be parsed out of a string.</summary>
internal sealed record BirdAddress(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public override string ToString() => "BirdAddress";
}

internal sealed record BirdAttachment(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("content_type")] string ContentType)
{
    public override string ToString() => "BirdAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BirdRequest))]
internal sealed partial class BirdJsonContext : JsonSerializerContext;
