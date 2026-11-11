using System.Text.Json.Serialization;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

/// <summary>The body of <c>POST /v2/notifications/email</c>. Null members are left out.</summary>
internal sealed record NotifyRequest(
    [property: JsonPropertyName("email_address")] string EmailAddress,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("personalisation")] NotifyPersonalisation Personalisation,
    [property: JsonPropertyName("email_reply_to_id")] string? EmailReplyToId)
{
    // The generated ToString would print the address, subject and body.
    public override string ToString() => "NotifyRequest";
}

internal sealed record NotifyPersonalisation(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body)
{
    public override string ToString() => "NotifyPersonalisation";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NotifyRequest))]
internal sealed partial class NotifyJsonContext : JsonSerializerContext;
