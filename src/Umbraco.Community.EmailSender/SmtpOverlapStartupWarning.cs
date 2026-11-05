using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Community.EmailSender;

/// <summary>
/// Q2 in the design: when a provider and SMTP (or a pickup folder) are both configured, the provider wins. Say so once,
/// because a developer who leaves a pickup folder set would otherwise wonder why nothing lands in it.
/// </summary>
internal sealed class SmtpOverlapStartupWarning(
    IOptionsMonitor<GlobalSettings> globalSettings,
    ILogger<SmtpOverlapStartupWarning> logger) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var settings = globalSettings.CurrentValue;
        if (settings.IsSmtpServerConfigured || settings.IsPickupDirectoryLocationConfigured)
        {
            logger.LogWarning(
                "EmailSender:Provider is set, so email goes through the provider and the configured SMTP host or pickup folder is ignored. Unset EmailSender:Provider to use SMTP again.");
        }

        return Task.CompletedTask;
    }
}
