namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <param name="Number">1 for the first attempt; at most <see cref="ProviderPipeline.MaxAttempts"/>.</param>
/// <param name="IdempotencyKey">One GUID per logical send, the same on every attempt. Sent only where the provider documents it.</param>
internal sealed record SendAttempt(int Number, string IdempotencyKey);
