using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Providers.SendGrid;

internal static class SendGridServiceCollectionExtensions
{
    public static IServiceCollection AddSendGridProvider(this IServiceCollection services)
    {
        services.AddEmailProviderClient(SendGridMapper.ClientName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProviderMapper, SendGridMapper>();
        services.AddSingleton<IEmailProvider, PipelineEmailProvider>();
        return services;
    }
}
