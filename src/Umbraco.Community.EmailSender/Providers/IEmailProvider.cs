using Umbraco.Cms.Core.HealthChecks;

namespace Umbraco.Community.EmailSender.Providers;

/// <summary>One HTTP email API. Implementations are singletons over <c>IHttpClientFactory</c> and <c>IOptionsMonitor</c>.</summary>
internal interface IEmailProvider
{
    /// <summary>Display name used in logs and error text, for example "Postmark".</summary>
    string Name { get; }

    /// <exception cref="EmailSendException">The provider refused the email or could not be reached.</exception>
    Task SendAsync(OutboundEmail email, CancellationToken ct);

    Task<HealthCheckStatus> CheckAsync(CancellationToken ct);
}
