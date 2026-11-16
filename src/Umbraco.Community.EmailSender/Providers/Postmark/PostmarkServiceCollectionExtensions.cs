using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.Postmark;

internal static class PostmarkServiceCollectionExtensions
{
    public static IServiceCollection AddPostmarkProvider(this IServiceCollection services)
    {
        services.AddEmailProviderClient(PostmarkMapper.ClientName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProviderMapper, PostmarkMapper>();
        services.AddSingleton<IEmailProvider, PipelineEmailProvider>();
        return services;
    }
}
