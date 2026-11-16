using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>
/// The provider-specific half of a send (design 9.2): build one request, read one response. Everything shared (caps,
/// retries, budget, logging) lives in <see cref="ProviderPipeline"/>, so a mapper never sleeps, retries or logs.
/// </summary>
internal interface IProviderMapper
{
    /// <summary>Display name used in logs and error text, for example "Postmark".</summary>
    string Name { get; }

    /// <summary>The named client, registered through <see cref="ProviderHttpClients.AddEmailProviderClient"/>.</summary>
    string ClientName { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>False when the service fixes the sender (GOV.UK Notify, Microsoft Graph), so no From is needed.</summary>
    bool UsesFromAddress => true;

    /// <summary>
    /// Called once per attempt: a request cannot be sent twice. The URI is absolute and built from the current options, so a
    /// region change applies at the next send. Throws <see cref="EmailSendException"/> for a problem the options cause.
    /// </summary>
    HttpRequestMessage BuildRequest(OutboundEmail email, EmailSenderOptions options, SendAttempt attempt);

    /// <summary>Decides accepted, rejected or unreadable. <paramref name="body"/> is already capped at 64 KB.</summary>
    ProviderOutcome ReadOutcome(HttpResponseMessage response, ReadOnlyMemory<byte> body);

    /// <summary>A read-only request that proves the credentials, or null when the provider has none (design 9.6).</summary>
    HttpRequestMessage? BuildHealthRequest(EmailSenderOptions options) => null;

    HealthCheckStatus ReadHealth(HttpResponseMessage response, ReadOnlyMemory<byte> body)
        => response.IsSuccessStatusCode
            ? new HealthCheckStatus($"{Name} accepted the credentials.") { ResultType = StatusResultType.Success }
            : new HealthCheckStatus($"{Name} answered HTTP {(int)response.StatusCode}.") { ResultType = StatusResultType.Error };
}
