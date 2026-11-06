using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers;

namespace Umbraco.Community.EmailSender;

/// <summary>Maps an <see cref="EmailMessage"/> to an <see cref="OutboundEmail"/> and hands it to the configured provider.</summary>
internal sealed class HttpApiEmailSenderClient(
    IOptionsMonitor<EmailSenderOptions> options,
    IOptionsMonitor<GlobalSettings> globalSettings,
    IEmailProvider provider,
    TimeProvider clock,
    ILogger<HttpApiEmailSenderClient> logger) : IEmailSenderClient
{
    public async Task SendAsync(EmailMessage message, TimeSpan? expires)
    {
        var current = options.CurrentValue;
        var started = Stopwatch.GetTimestamp();
        OutboundEmail email;
        try
        {
            email = OutboundEmail.Create(
                message,
                current.From,
                globalSettings.CurrentValue.Smtp?.From,
                expires,
                current.MaxTotalAttachmentBytes,
                clock,
                provider.UsesFromAddress);

            await provider.SendAsync(email, CancellationToken.None);
        }
        catch (EmailSendException ex)
        {
            // Recipients, subject and bodies stay out of logs (GDPR); the exception text already names provider and status.
            logger.LogWarning("{Provider} send failed: {Reason}", provider.Name, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Only the type name travels on: an HttpRequestException or JsonException can echo an address or a body.
            var wrapped = new EmailSendException($"{provider.Name} send failed: {ex.GetType().Name}.");
            logger.LogWarning("{Provider} send failed: {Reason}", provider.Name, wrapped.Message);
            throw wrapped;
        }

        logger.LogInformation(
            "{Provider} accepted an email: {RecipientCount} recipients, {AttachmentCount} attachments, {ElapsedMs} ms",
            provider.Name,
            email.To.Count + email.Cc.Count + email.Bcc.Count,
            email.Attachments.Count,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

#if UMBRACO_17
    [Obsolete("Core's obsolete overload; kept because the 17 line's interface still declares it.")]
    public Task SendAsync(EmailMessage message) => SendAsync(message, null);
#endif
}
