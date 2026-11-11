using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;

internal static class AcsServiceCollectionExtensions
{
    public static IServiceCollection AddAcsProvider(this IServiceCollection services)
    {
        // The SDK keeps one HttpClient for the life of its EmailClient, so the handler must neither be recycled under it
        // (infinite handler lifetime) nor pin DNS forever (pooled connections expire).
        services.AddHttpClient(AcsProvider.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            // The factory logs headers at Trace: the rendered text is redacted but the structured state keeps the raw value, so a
            // structured log sink would receive the signed Authorization header. This package logs for itself.
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailProvider, AcsProvider>();
        return services;
    }
}
