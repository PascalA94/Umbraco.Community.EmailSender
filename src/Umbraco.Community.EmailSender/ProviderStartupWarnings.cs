using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender;

/// <summary>
/// Logs the selected provider's own start-up warnings once (design 9.3): a key prefix that does not match the configured region
/// is a hint, not a failure, because key formats change. The text is fixed and never carries the key.
/// </summary>
internal sealed class ProviderStartupWarnings(
    IOptionsMonitor<EmailSenderOptions> options,
    IEnumerable<IProviderMapper> mappers,
    ILogger<ProviderStartupWarnings> logger) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        foreach (var mapper in mappers)
        {
            foreach (var warning in mapper.StartupWarnings(current))
            {
                logger.LogWarning("{Warning}", warning);
            }
        }

        return Task.CompletedTask;
    }
}
