using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

internal static class PostmarkServiceCollectionExtensions
{
    public static IServiceCollection AddPostmarkProvider(this IServiceCollection services)
    {
        services.AddHttpClient(PostmarkProvider.ClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.postmarkapp.com/");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                // PostmarkProvider enforces EmailSender:TimeoutSeconds itself.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // Factory logging prints header values at Trace; the server token must never reach a log.
            .RedactLoggedHeaders(_ => true)
            // A redirect would carry the server token to another host, so none is followed.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailProvider, PostmarkProvider>();
        return services;
    }
}
