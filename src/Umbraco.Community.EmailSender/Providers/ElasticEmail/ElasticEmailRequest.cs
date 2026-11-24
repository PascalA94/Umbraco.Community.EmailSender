using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.ElasticEmail;

/// <summary>The body of <c>POST /v4/emails/transactional</c>: nested <c>Recipients</c>, <c>Content</c> and <c>Options</c>.</summary>
internal sealed record ElasticEmailRequest(
    [property: JsonPropertyName("Recipients")] ElasticEmailRecipients Recipients,
    [property: JsonPropertyName("Content")] ElasticEmailContent Content,
    [property: JsonPropertyName("Options")] ElasticEmailOptionsBody? Options)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "ElasticEmailRequest";
}

internal sealed record ElasticEmailRecipients(
    [property: JsonPropertyName("To")] IReadOnlyList<string> To,
    [property: JsonPropertyName("CC")] IReadOnlyList<string>? Cc,
    [property: JsonPropertyName("BCC")] IReadOnlyList<string>? Bcc)
{
    public override string ToString() => "ElasticEmailRecipients";
}

internal sealed record ElasticEmailContent(
    [property: JsonPropertyName("From")] string From,
    [property: JsonPropertyName("ReplyTo")] string? ReplyTo,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("Body")] IReadOnlyList<ElasticEmailBodyPart> Body,
    [property: JsonPropertyName("Headers")] IReadOnlyDictionary<string, string>? Headers,
    [property: JsonPropertyName("Attachments")] IReadOnlyList<ElasticEmailAttachment>? Attachments)
{
    public override string ToString() => "ElasticEmailContent";
}

/// <summary>One part per body kind. <c>ContentType</c> is <c>HTML</c> or <c>PlainText</c>.</summary>
internal sealed record ElasticEmailBodyPart(
    [property: JsonPropertyName("ContentType")] string ContentType,
    [property: JsonPropertyName("Content")] string Content,
    [property: JsonPropertyName("Charset")] string Charset)
{
    public override string ToString() => "ElasticEmailBodyPart";
}

internal sealed record ElasticEmailAttachment(
    [property: JsonPropertyName("BinaryContent")] string BinaryContent,
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("ContentType")] string ContentType)
{
    public override string ToString() => "ElasticEmailAttachment";
}

internal sealed record ElasticEmailOptionsBody(
    [property: JsonPropertyName("TrackOpens")] bool TrackOpens,
    [property: JsonPropertyName("TrackClicks")] bool TrackClicks)
{
    public override string ToString() => "ElasticEmailOptionsBody";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElasticEmailRequest))]
internal sealed partial class ElasticEmailJsonContext : JsonSerializerContext;
