using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

/// <summary>The body of <c>POST /email</c>. Null members are left out so Postmark's own defaults apply.</summary>
internal sealed record PostmarkRequest(
    string From,
    string To,
    string? Cc,
    string? Bcc,
    string? ReplyTo,
    string Subject,
    string? HtmlBody,
    string? TextBody,
    string MessageStream,
    bool? TrackOpens,
    string? TrackLinks,
    IReadOnlyList<PostmarkHeader>? Headers,
    IReadOnlyList<PostmarkAttachment>? Attachments)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "PostmarkRequest";
}

internal sealed record PostmarkHeader(string Name, string Value)
{
    public override string ToString() => "PostmarkHeader";
}

internal sealed record PostmarkAttachment(string Name, string Content, string ContentType)
{
    public override string ToString() => "PostmarkAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PostmarkRequest))]
internal sealed partial class PostmarkJsonContext : JsonSerializerContext;
