using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

internal static class PostmarkValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        var postmark = options.Postmark;
        if (string.IsNullOrWhiteSpace(postmark.ServerToken))
        {
            failures.Add("EmailSender:Postmark:ServerToken is required when EmailSender:Provider is Postmark.");
        }
        // A trailing newline from a file-mounted secret would make every send fail with a bare FormatException.
        else if (postmark.ServerToken.Any(c => char.IsControl(c) || c > 126))
        {
            failures.Add("EmailSender:Postmark:ServerToken contains a control or non-ASCII character (a trailing newline?).");
        }
        else if (string.IsNullOrWhiteSpace(postmark.MessageStream))
        {
            failures.Add("EmailSender:Postmark:MessageStream must not be blank.");
        }
    }
}
