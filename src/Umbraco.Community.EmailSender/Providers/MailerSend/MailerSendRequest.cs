using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.MailerSend;

/// <summary>The body of <c>POST /v1/email</c>. Null members are left out so MailerSend's own defaults apply.</summary>
internal sealed record MailerSendRequest(
    [property: JsonPropertyName("from")] MailerSendAddress From,
    [property: JsonPropertyName("to")] IReadOnlyList<MailerSendAddress> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<MailerSendAddress>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<MailerSendAddress>? Bcc,
    [property: JsonPropertyName("reply_to")] MailerSendAddress? ReplyTo,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("attachments")] IReadOnlyList<MailerSendAttachment>? Attachments,
    [property: JsonPropertyName("settings")] MailerSendSettings? Settings)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "MailerSendRequest";
}

internal sealed record MailerSendAddress(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("name")] string? Name = null)
{
    public override string ToString() => "MailerSendAddress";
}

/// <summary>There is no content-type field: MailerSend derives the type from the file name.</summary>
internal sealed record MailerSendAttachment(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("disposition")] string Disposition)
{
    public override string ToString() => "MailerSendAttachment";
}

internal sealed record MailerSendSettings(
    [property: JsonPropertyName("track_opens")] bool TrackOpens,
    [property: JsonPropertyName("track_clicks")] bool TrackClicks)
{
    public override string ToString() => "MailerSendSettings";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MailerSendRequest))]
internal sealed partial class MailerSendJsonContext : JsonSerializerContext;
