using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.MicrosoftGraph;

internal static class MicrosoftGraphServiceCollectionExtensions
{
    /// <summary>Two named clients (the token endpoint and Graph), both through the shared registration, and one token cache.</summary>
    public static IServiceCollection AddMicrosoftGraphProvider(this IServiceCollection services)
    {
        services.AddEmailProviderClient(GraphTokenSource.ClientName);
        services.TryAddSingleton<GraphTokenSource>();
        return services.AddPipelineProvider<GraphMapper>(GraphMapper.ClientName);
    }
}
