using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Community.EmailSender.Configuration;
using MimeKit;

namespace Umbraco.Community.EmailSender;

/// <summary>
/// Replaces core's <c>EmailSender</c>. Core returns before calling the client unless <c>Smtp:Host</c> or a pickup folder
/// is set, so replacing only the client would send nothing on a site without SMTP (design section 0 and 4.1). This keeps
/// core's order (notification first, then send) without the SMTP and pickup branches.
/// </summary>
internal sealed class HttpApiEmailSender : IEmailSender
{
    private readonly IEventAggregator _events;
    private readonly IEmailSenderClient _client;
    private readonly IOptionsMonitor<GlobalSettings> _globalSettings;
    private readonly IOptionsMonitor<EmailSenderOptions> _options;
    private readonly ILogger<HttpApiEmailSender> _logger;

    public HttpApiEmailSender(
        IEventAggregator events,
        IEmailSenderClient client,
        IOptionsMonitor<GlobalSettings> globalSettings,
        IOptionsMonitor<EmailSenderOptions> options,
        ILogger<HttpApiEmailSender> logger)
    {
        _events = events;
        _client = client;
        _globalSettings = globalSettings;
        _options = options;
        _logger = logger;
    }

    public bool CanSendRequiredEmail() => true;

#if UMBRACO_17
    // The 17 line's interface still declares these two overloads without default bodies.
    [Obsolete("Core's obsolete overload; kept because the 17 line's interface still declares it.")]
    public Task SendAsync(EmailMessage message, string emailType) => SendAsync(message, emailType, false, null);

    [Obsolete("Core's obsolete overload; kept because the 17 line's interface still declares it.")]
    public Task SendAsync(EmailMessage message, string emailType, bool enableNotification)
        => SendAsync(message, emailType, enableNotification, null);
#endif

    public async Task SendAsync(EmailMessage message, string emailType, bool enableNotification = false, TimeSpan? expires = null)
    {
        if (enableNotification)
        {
            var notification = new SendEmailNotification(ToNotificationEmail(message, _options.CurrentValue.From, _globalSettings.CurrentValue.Smtp?.From), emailType);
            await _events.PublishAsync(notification);

            if (notification.IsHandled)
            {
                _logger.LogDebug("An email of type {EmailType} was handled by a notification handler", emailType);
                return;
            }
        }

        await _client.SendAsync(message, expires);
    }

    // Core's equivalent extension is internal, so this mirrors it: handlers see the same model they get from core's sender.
    private static NotificationEmailModel ToNotificationEmail(EmailMessage message, string? optionsFrom, string? smtpFrom)
        => new(
            ToAddress(new[] { message.From, optionsFrom, smtpFrom }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))),
            ToAddresses(message.To),
            ToAddresses(message.Cc),
            ToAddresses(message.Bcc),
            ToAddresses(message.ReplyTo),
            message.Subject,
            message.Body,
            message.Attachments,
            message.IsBodyHtml);

    private static NotificationEmailAddress? ToAddress(string? address)
        => InternetAddress.TryParse(address, out var parsed) && parsed is MailboxAddress mailbox
            ? new NotificationEmailAddress(mailbox.Address, parsed.Name ?? string.Empty)
            : null;

    private static IEnumerable<NotificationEmailAddress>? ToAddresses(IEnumerable<string?>? addresses)
        => addresses?.Select(ToAddress).OfType<NotificationEmailAddress>().ToList();
}
