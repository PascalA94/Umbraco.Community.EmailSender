using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.GovUkNotify;
using Umbraco.Community.EmailSender.Providers.Postmark;

namespace Umbraco.Community.EmailSender.Providers.Registry;

/// <summary>
/// The one list of providers. Every <see cref="EmailProviderKind"/> value has exactly one definition here; RegistryTests
/// fails otherwise, so no provider can skip validation, the conformance suite or the leak sweep.
/// </summary>
internal static class ProviderRegistry
{
    public static IReadOnlyList<ProviderDefinition> All { get; } =
    [
        new(EmailProviderKind.AzureCommunicationServices, nameof(EmailProviderKind.AzureCommunicationServices), AcsValidation.Validate, s => s.AddAcsProvider()),
        new(EmailProviderKind.Postmark, nameof(EmailProviderKind.Postmark), PostmarkValidation.Validate, s => s.AddPostmarkProvider()),
        new(EmailProviderKind.GovUkNotify, nameof(EmailProviderKind.GovUkNotify), NotifyValidation.Validate, s => s.AddNotifyProvider()),
    ];

    public static ProviderDefinition Get(EmailProviderKind kind)
        => All.FirstOrDefault(d => d.Kind == kind)
           ?? throw new InvalidOperationException($"No provider definition for {kind}.");
}
