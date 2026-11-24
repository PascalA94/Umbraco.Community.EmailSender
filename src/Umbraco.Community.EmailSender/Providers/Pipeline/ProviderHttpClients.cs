using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

internal static class ProviderHttpClients
{
    private static readonly ProductInfoHeaderValue UserAgent = new("Umbraco.Community.EmailSender", PackageVersion());

    /// <summary>
    /// The only way a pipeline provider registers its client (design 9.2, "HTTP clients"). No base address: the mapper builds an
    /// absolute URI from the current options on every send.
    /// </summary>
    public static IHttpClientBuilder AddEmailProviderClient(this IServiceCollection services, string name)
        => services.AddHttpClient(name, client =>
            {
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                // Resend and Mailtrap refuse requests without a User-Agent.
                client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
                // The pipeline owns the budget, retries included (EmailSender:TimeoutSeconds).
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // The factory logs headers at Trace: the rendered text is redacted but the structured state keeps the raw value
            // (M3 finding), so a structured sink would receive the credential. The pipeline logs for itself.
            .RemoveAllLoggers()
            // A site's ConfigureHttpClientDefaults (Aspire ServiceDefaults' resilience handler, a Polly policy) would wrap this
            // client and resend POSTs: the pipeline owns retries, and a duplicate invite is worse than a failed one.
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear())
            // A redirect would carry the credential to another host, so none is followed.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

    /// <summary>The registration every pipeline provider after Notify shares: its client, the clock, its mapper and the pipeline provider.</summary>
    public static IServiceCollection AddPipelineProvider<TMapper>(this IServiceCollection services, string clientName)
        where TMapper : class, IProviderMapper
    {
        services.AddEmailProviderClient(clientName);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IProviderMapper, TMapper>();
        services.AddSingleton<IEmailProvider, PipelineEmailProvider>();
        return services;
    }

    private static string PackageVersion()
    {
        var assembly = typeof(ProviderHttpClients).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // The build appends "+<commit>"; a User-Agent needs only the package version.
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? assembly.GetName().Version?.ToString(3) ?? "0.0.0" : version;
    }
}
