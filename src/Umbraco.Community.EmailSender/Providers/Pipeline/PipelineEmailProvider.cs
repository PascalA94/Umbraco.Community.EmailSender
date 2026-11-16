using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>
/// <see cref="IEmailProvider"/> for every HTTP provider: a singleton over <c>IHttpClientFactory</c> and <c>IOptionsMonitor</c>
/// (pitfalls P1 and P2) that runs the mapper through the shared pipeline (design 9.2).
/// </summary>
internal sealed class PipelineEmailProvider(
    IProviderMapper mapper,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<EmailSenderOptions> options,
    TimeProvider clock,
    ILogger<PipelineEmailProvider> logger) : IEmailProvider
{
    public string Name => mapper.Name;

    public bool UsesFromAddress => mapper.UsesFromAddress;

    /// <summary>The source of back-off jitter (0 to 1). Tests fix it; the default is random.</summary>
    internal Func<double> Jitter { get; init; } = Random.Shared.NextDouble;

    internal IProviderMapper Mapper => mapper;

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        try
        {
            await SendCoreAsync(email, ct);
        }
        catch (Exception ex) when (ex is not (EmailSendException or OperationCanceledException))
        {
            // The type name only: HttpRequestException and FormatException text can echo the request or the key.
            throw new EmailSendException($"{Name} send failed: {ex.GetType().Name}.");
        }
    }

    private async Task SendCoreAsync(OutboundEmail email, CancellationToken ct)
    {
        var current = options.CurrentValue;
        ProviderPreChecks.Run(Name, email, mapper.Capabilities);
        var pipeline = new ProviderPipeline(mapper, httpClientFactory, clock, logger, Jitter);
        if (mapper.Capabilities.Recipients == RecipientPolicy.Batch)
        {
            await pipeline.SendAsync(email, current, ct);
            return;
        }

        // One request per recipient, each with its own budget (design 9.2, "Budget").
        var sent = 0;
        foreach (var recipient in email.To)
        {
            try
            {
                await pipeline.SendAsync(email with { To = [recipient] }, current, ct);
            }
            catch (Exception ex) when (sent > 0 && ex is not OperationCanceledException)
            {
                // Earlier recipients already have the email, so the caller must know before it retries.
                var reason = ex is EmailSendException ? ex.Message : $"{Name} send failed: {ex.GetType().Name}.";
                throw new EmailSendException($"{reason} Sent to {sent} of {email.To.Count} recipients before this failure.");
            }

            sent++;
        }
    }

    public async Task<HealthCheckStatus> CheckAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        try
        {
            using var request = mapper.BuildHealthRequest(current);
            if (request is null)
            {
                // A check that needs a full-access key would push users to widen their key (design 9.6).
                return new HealthCheckStatus($"{Name} is configured. Credentials are not checked without sending.") { ResultType = StatusResultType.Info };
            }

            return await ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct, async token =>
            {
                using var response = await httpClientFactory.CreateClient(mapper.ClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                var body = await ResponseReader.ReadCappedAsync(response, token);
                return mapper.ReadHealth(response, body);
            });
        }
        catch (EmailSendException ex)
        {
            return new HealthCheckStatus(ex.Message) { ResultType = StatusResultType.Error };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type name only: HttpRequestException text can echo the request.
            return new HealthCheckStatus($"{Name} could not be reached ({ex.GetType().Name}).") { ResultType = StatusResultType.Error };
        }
    }
}
