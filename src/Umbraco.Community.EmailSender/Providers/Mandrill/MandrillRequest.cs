using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Mandrill;

/// <summary>
/// The body of <c>POST /api/1.4/messages/send</c>. The API key is the top-level <c>key</c> field, so this is the one request
/// type that carries a credential: <see cref="ToString"/> never prints it. <c>images[]</c> is never sent (design 9.3).
/// </summary>
internal sealed record MandrillRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("message")] MandrillMessage Message)
{
    public override string ToString() => "MandrillRequest";
}

internal sealed record MandrillMessage(
    [property: JsonPropertyName("from_email")] string FromEmail,
    [property: JsonPropertyName("from_name")] string? FromName,
    [property: JsonPropertyName("to")] IReadOnlyList<MandrillRecipient> To,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("attachments")] IReadOnlyList<MandrillAttachment>? Attachments,
    [property: JsonPropertyName("track_opens")] bool? TrackOpens,
    [property: JsonPropertyName("track_clicks")] bool? TrackClicks)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "MandrillMessage";
}

/// <summary><c>Type</c> is <c>to</c>, <c>cc</c> or <c>bcc</c>: one array holds every recipient.</summary>
internal sealed record MandrillRecipient(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("type")] string Type)
{
    public override string ToString() => "MandrillRecipient";
}

internal sealed record MandrillAttachment(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("content")] string Content)
{
    public override string ToString() => "MandrillAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MandrillRequest))]
internal sealed partial class MandrillJsonContext : JsonSerializerContext;
