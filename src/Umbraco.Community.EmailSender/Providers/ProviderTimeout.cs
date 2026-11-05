namespace Umbraco.Community.EmailSender.Providers;

internal static class ProviderTimeout
{
    /// <summary>
    /// Enforces <c>EmailSender:TimeoutSeconds</c> because the client passes no token of its own. A cancellation by the
    /// caller stays a cancellation; only the timeout becomes an <see cref="EmailSendException"/>.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        string providerName,
        int timeoutSeconds,
        TimeProvider clock,
        CancellationToken ct,
        Func<CancellationToken, Task<T>> work)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await work(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new EmailSendException($"{providerName} did not answer within {timeoutSeconds} seconds.");
        }
    }
}
