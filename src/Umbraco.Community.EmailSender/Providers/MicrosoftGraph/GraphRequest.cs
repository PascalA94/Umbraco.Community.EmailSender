using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

/// <summary>The body of <c>POST /users/{id}/sendMail</c>. Null members are left out so Graph's own defaults apply.</summary>
internal sealed record GraphRequest(
    [property: JsonPropertyName("message")] GraphMessage Message,
    [property: JsonPropertyName("saveToSentItems")] bool SaveToSentItems)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "GraphRequest";
}

internal sealed record GraphMessage(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] GraphBody Body,
    [property: JsonPropertyName("toRecipients")] IReadOnlyList<GraphRecipient> ToRecipients,
    [property: JsonPropertyName("ccRecipients")] IReadOnlyList<GraphRecipient>? CcRecipients,
    [property: JsonPropertyName("bccRecipients")] IReadOnlyList<GraphRecipient>? BccRecipients,
    [property: JsonPropertyName("replyTo")] IReadOnlyList<GraphRecipient>? ReplyTo,
    [property: JsonPropertyName("internetMessageHeaders")] IReadOnlyList<GraphHeader>? InternetMessageHeaders,
    [property: JsonPropertyName("attachments")] IReadOnlyList<GraphAttachment>? Attachments)
{
    public override string ToString() => "GraphMessage";
}

internal sealed record GraphBody(
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("content")] string Content)
{
    public override string ToString() => "GraphBody";
}

internal sealed record GraphRecipient([property: JsonPropertyName("emailAddress")] GraphEmailAddress EmailAddress)
{
    public override string ToString() => "GraphRecipient";
}

internal sealed record GraphEmailAddress([property: JsonPropertyName("address")] string Address)
{
    public override string ToString() => "GraphEmailAddress";
}

internal sealed record GraphHeader(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value)
{
    public override string ToString() => "GraphHeader";
}

internal sealed record GraphAttachment(
    [property: JsonPropertyName("@odata.type")] string ODataType,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("contentBytes")] string ContentBytes)
{
    public override string ToString() => "GraphAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphRequest))]
internal sealed partial class GraphJsonContext : JsonSerializerContext;
