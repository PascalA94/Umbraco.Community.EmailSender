using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.SendGrid;

/// <summary>The body of <c>POST /v3/mail/send</c> (design 9.2). Null members are left out.</summary>
internal sealed record SendGridRequest(
    [property: JsonPropertyName("personalizations")] IReadOnlyList<SendGridPersonalization> Personalizations,
    [property: JsonPropertyName("from")] SendGridAddress From,
    [property: JsonPropertyName("reply_to_list")] IReadOnlyList<SendGridAddress>? ReplyToList,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("content")] IReadOnlyList<SendGridContent> Content,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("attachments")] IReadOnlyList<SendGridAttachment>? Attachments,
    [property: JsonPropertyName("tracking_settings")] SendGridTracking? TrackingSettings)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "SendGridRequest";
}

internal sealed record SendGridPersonalization(
    [property: JsonPropertyName("to")] IReadOnlyList<SendGridAddress> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<SendGridAddress>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<SendGridAddress>? Bcc)
{
    public override string ToString() => "SendGridPersonalization";
}

internal sealed record SendGridAddress(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public override string ToString() => "SendGridAddress";
}

internal sealed record SendGridContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value)
{
    public override string ToString() => "SendGridContent";
}

internal sealed record SendGridAttachment(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("disposition")] string Disposition)
{
    public override string ToString() => "SendGridAttachment";
}

internal sealed record SendGridTracking(
    [property: JsonPropertyName("click_tracking")] SendGridClickTracking ClickTracking,
    [property: JsonPropertyName("open_tracking")] SendGridToggle OpenTracking);

internal sealed record SendGridClickTracking(
    [property: JsonPropertyName("enable")] bool Enable,
    [property: JsonPropertyName("enable_text")] bool EnableText);

internal sealed record SendGridToggle([property: JsonPropertyName("enable")] bool Enable);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SendGridRequest))]
internal sealed partial class SendGridJsonContext : JsonSerializerContext;
