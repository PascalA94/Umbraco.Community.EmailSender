namespace Umbraco.Community.EmailSender.Providers;

internal sealed record OutboundAttachment(string Name, string ContentType, string Base64)
{
    // A record's generated ToString prints every member, including the file name and content.
    public override string ToString() => $"OutboundAttachment {{ Length = {Base64.Length} }}";
}
