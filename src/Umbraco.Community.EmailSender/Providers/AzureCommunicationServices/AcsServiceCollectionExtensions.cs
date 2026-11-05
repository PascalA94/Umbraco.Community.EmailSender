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
            // Factory logging prints header values at Trace, and the signed Authorization header is one.
            .RedactLoggedHeaders(_ => true)
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
