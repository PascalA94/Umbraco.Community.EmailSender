using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

internal static class NotifyValidation
{
    public static void Validate(EmailSenderOptions options, List<string> failures)
    {
        var notify = options.GovUkNotify;
        if (string.IsNullOrWhiteSpace(notify.ApiKey))
        {
            failures.Add("EmailSender:GovUkNotify:ApiKey is required when EmailSender:Provider is GovUkNotify.");
        }
        else if (!NotifyApiKey.TryParse(notify.ApiKey, out _, out _))
        {
            // The value is never echoed: a mistyped key still looks like a secret. A trailing newline is the usual cause.
            failures.Add("EmailSender:GovUkNotify:ApiKey is not a GOV.UK Notify API key (a trailing newline or a truncated key?).");
        }

        if (!Guid.TryParseExact(notify.EmailTemplateId?.Trim(), "D", out _))
        {
            failures.Add("EmailSender:GovUkNotify:EmailTemplateId must be the template's id (a GUID).");
        }

        if (!string.IsNullOrWhiteSpace(notify.ReplyToId) && !Guid.TryParseExact(notify.ReplyToId.Trim(), "D", out _))
        {
            failures.Add("EmailSender:GovUkNotify:ReplyToId must be a reply-to address id (a GUID) or left empty.");
        }
    }
}
