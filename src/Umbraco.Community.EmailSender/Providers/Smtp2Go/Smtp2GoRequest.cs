using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.Smtp2Go;

/// <summary>
/// The body of <c>POST /v3/email/send</c>. <c>fastaccept</c> is never sent: without it SMTP2GO reports refusals in the response,
/// and with it a 200 would hide them. Null members are left out so SMTP2GO's own defaults apply.
/// </summary>
internal sealed record Smtp2GoRequest(
    [property: JsonPropertyName("sender")] string Sender,
    [property: JsonPropertyName("to")] IReadOnlyList<string> To,
    [property: JsonPropertyName("cc")] IReadOnlyList<string>? Cc,
    [property: JsonPropertyName("bcc")] IReadOnlyList<string>? Bcc,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("html_body")] string? HtmlBody,
    [property: JsonPropertyName("text_body")] string? TextBody,
    [property: JsonPropertyName("custom_headers")] IReadOnlyList<Smtp2GoHeader>? CustomHeaders,
    [property: JsonPropertyName("attachments")] IReadOnlyList<Smtp2GoAttachment>? Attachments)
{
    // The generated ToString would print addresses, subject and body.
    public override string ToString() => "Smtp2GoRequest";
}

internal sealed record Smtp2GoHeader(
    [property: JsonPropertyName("header")] string Header,
    [property: JsonPropertyName("value")] string Value)
{
    public override string ToString() => "Smtp2GoHeader";
}

internal sealed record Smtp2GoAttachment(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("fileblob")] string FileBlob,
    [property: JsonPropertyName("mimetype")] string MimeType)
{
    public override string ToString() => "Smtp2GoAttachment";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Smtp2GoRequest))]
internal sealed partial class Smtp2GoJsonContext : JsonSerializerContext;
