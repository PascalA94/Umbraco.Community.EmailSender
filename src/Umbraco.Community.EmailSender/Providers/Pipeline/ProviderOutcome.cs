using System.Net;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>What a mapper read from one response. A 2xx is not proof of acceptance (design 9.2, body-parse rules).</summary>
internal abstract record ProviderOutcome
{
    public static ProviderOutcome Unreadable { get; } = new UnreadableOutcome();

    /// <param name="ids">Provider message ids; empty when the provider returns none.</param>
    /// <param name="warning">Fixed text logged at Warning, for example a paused account. Never provider text.</param>
    public static ProviderOutcome Accepted(IReadOnlyList<string> ids, string? warning = null) => new AcceptedOutcome(ids, warning);

    /// <param name="status">The HTTP status.</param>
    /// <param name="code">Raw provider code; the pipeline drops it unless it passes <see cref="ResponseReader.SafeCode"/>.</param>
    /// <param name="hint">Fixed text per mapper, for example "Check EmailSender:Resend:ApiKey.".</param>
    /// <param name="retryable">False forbids a retry. True only allows one: the pipeline's table (design 9.2) still decides.</param>
    /// <param name="quotaStop">A 429 that will not clear by waiting (a daily or monthly quota).</param>
    public static ProviderOutcome Rejected(int status, string? code, string? hint, bool retryable, bool quotaStop)
        => new RejectedOutcome(status, code, hint, retryable, quotaStop);

    /// <summary>The design 9.2 table: 429 (unless a quota stop), 420 and 503 are not-accepted signals; nothing else is.</summary>
    /// <remarks>500, 502 and 504 are marked retryable too: the pipeline repeats them only when the provider has an idempotency key.</remarks>
    public static ProviderOutcome ForStatus(HttpStatusCode status, string? code, string? hint, bool quotaStop = false)
        => Rejected((int)status, code, hint, retryable: !quotaStop && (int)status is 429 or 420 or 503 or 500 or 502 or 504, quotaStop);

    public sealed record AcceptedOutcome(IReadOnlyList<string> Ids, string? Warning) : ProviderOutcome;

    public sealed record RejectedOutcome(int Status, string? Code, string? Hint, bool Retryable, bool QuotaStop) : ProviderOutcome
    {
        // The raw code may be provider text that failed the allow-list.
        public override string ToString() => $"RejectedOutcome {{ Status = {Status} }}";
    }

    public sealed record UnreadableOutcome : ProviderOutcome;
}
