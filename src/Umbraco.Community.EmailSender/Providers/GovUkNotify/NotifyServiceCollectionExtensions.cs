using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

internal static class NotifyServiceCollectionExtensions
{
    public static IServiceCollection AddNotifyProvider(this IServiceCollection services)
    {
        services.AddHttpClient(NotifyProvider.ClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.notifications.service.gov.uk/");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                // NotifyProvider enforces EmailSender:TimeoutSeconds itself.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // The factory logs headers at Trace: the rendered text is redacted but the structured state keeps the raw value, so a
            // structured log sink would receive the credential. This package logs for itself (counts and ids only).
            .RemoveAllLoggers()
            // A redirect would carry the bearer token to another host, so none is followed.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailProvider, NotifyProvider>();
        return services;
    }
}
