namespace Umbraco.Community.EmailSender;

/// <summary>
/// A send failed. The message names the provider and status only: never a recipient, subject, key or body.
/// </summary>
public sealed class EmailSendException : Exception
{
    public EmailSendException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
