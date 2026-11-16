using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

internal static class NotifyServiceCollectionExtensions
{
    public static IServiceCollection AddNotifyProvider(this IServiceCollection services)
    {
        services.AddEmailProviderClient(NotifyMapper.ClientName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProviderMapper, NotifyMapper>();
        services.AddSingleton<IEmailProvider, PipelineEmailProvider>();
        return services;
    }
}
