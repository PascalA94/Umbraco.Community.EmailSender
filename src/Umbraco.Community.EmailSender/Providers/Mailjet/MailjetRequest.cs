using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Mailjet;

/// <summary>The body of <c>POST /v3.1/send</c>: exactly one message, and <c>SandboxMode</c> is never sent.</summary>
internal sealed record MailjetRequest([property: JsonPropertyName("Messages")] IReadOnlyList<MailjetMessage> Messages)
{
    public override string ToString() => "MailjetRequest";
}

internal sealed record MailjetMessage(
    [property: JsonPropertyName("From")] MailjetAddress From,
    [property: JsonPropertyName("To")] IReadOnlyList<MailjetAddress> To,
    [property: JsonPropertyName("Cc")] IReadOnlyList<MailjetAddress>? Cc,
    [property: JsonPropertyName("Bcc")] IReadOnlyList<MailjetAddress>? Bcc,
    [property: JsonPropertyName("ReplyTo")] MailjetAddress? ReplyTo,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("TextPart")] string? TextPart,
    [property: JsonPropertyName("HTMLPart")] string? HtmlPart,
    [property: JsonPropertyName("Headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("Attachments")] IReadOnlyList<MailjetAttachment>? Attachments,
    [property: JsonPropertyName("TrackOpens")] string? TrackOpens,
    [property: JsonPropertyName("TrackClicks")] string? TrackClicks)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "MailjetMessage";
}

internal sealed record MailjetAddress(
    [property: JsonPropertyName("Email")] string Email,
    [property: JsonPropertyName("Name")] string? Name = null)
{
    public override string ToString() => "MailjetAddress";
}

internal sealed record MailjetAttachment(
    [property: JsonPropertyName("ContentType")] string ContentType,
    [property: JsonPropertyName("Filename")] string Filename,
    [property: JsonPropertyName("Base64Content")] string Base64Content)
{
    public override string ToString() => "MailjetAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MailjetRequest))]
internal sealed partial class MailjetJsonContext : JsonSerializerContext;
