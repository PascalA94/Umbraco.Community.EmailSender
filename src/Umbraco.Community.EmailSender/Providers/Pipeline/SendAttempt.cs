namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <param name="Number">1 for the first attempt; at most <see cref="ProviderPipeline.MaxAttempts"/>.</param>
/// <param name="IdempotencyKey">One GUID per logical send, the same on every attempt. Sent only where the provider documents it.</param>
/// <param name="Credential">A short-lived credential fetched for this attempt by <see cref="IProviderMapper.AcquireCredentialAsync"/> (Graph's token); null for a provider with a static key.</param>
internal sealed record SendAttempt(int Number, string IdempotencyKey, string? Credential = null)
{
    // The generated ToString would print the credential.
    public override string ToString() => $"SendAttempt {{ Number = {Number} }}";
}
