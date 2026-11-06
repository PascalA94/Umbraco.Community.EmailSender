using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Providers;

namespace Umbraco.Community.EmailSender.HealthChecks;

/// <summary>
/// "Email provider configured and reachable". Health checks are discovered by type scan whether or not the composer
/// registered a provider, so the provider is looked up lazily: a constructor dependency on it would fail every health
/// check on a site that has not set <c>EmailSender:Provider</c>.
/// </summary>
[HealthCheck("e55002a9-087e-4df5-a0d2-72a8d546fa72", "Email provider", Description = "Checks that the configured HTTP email API accepts this site's credentials.", Group = "Services")]
public sealed class EmailProviderHealthCheck(IServiceProvider services) : HealthCheck
{
    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var provider = services.GetService<IEmailProvider>();
        if (provider is null)
        {
            return [new HealthCheckStatus("EmailSender:Provider is not set, so Umbraco's own SMTP settings send the email.") { ResultType = StatusResultType.Info }];
        }

        try
        {
            return [await provider.CheckAsync(CancellationToken.None)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type name only: an HttpRequestException can echo the request.
            return [new HealthCheckStatus($"{provider.Name} check failed ({ex.GetType().Name}).") { ResultType = StatusResultType.Error }];
        }
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        => throw new InvalidOperationException("This health check has no actions.");
}
