using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.Brevo;
using Umbraco.Community.EmailSender.Providers.Mailgun;
using Umbraco.Community.EmailSender.Providers.Mailjet;
using Umbraco.Community.EmailSender.Providers.Pipeline;
using Umbraco.Community.EmailSender.Providers.GovUkNotify;
using Umbraco.Community.EmailSender.Providers.Postmark;
using Umbraco.Community.EmailSender.Providers.Resend;
using Umbraco.Community.EmailSender.Providers.SendGrid;

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
        new(EmailProviderKind.SendGrid, nameof(EmailProviderKind.SendGrid), SendGridValidation.Validate, s => s.AddSendGridProvider()),
        new(EmailProviderKind.Resend, nameof(EmailProviderKind.Resend), ResendValidation.Validate, s => s.AddPipelineProvider<ResendMapper>(ResendMapper.ClientName)),
        new(EmailProviderKind.Mailgun, nameof(EmailProviderKind.Mailgun), MailgunValidation.Validate, s => s.AddPipelineProvider<MailgunMapper>(MailgunMapper.ClientName)),
        new(EmailProviderKind.Brevo, nameof(EmailProviderKind.Brevo), BrevoValidation.Validate, s => s.AddPipelineProvider<BrevoMapper>(BrevoMapper.ClientName)),
        new(EmailProviderKind.Mailjet, nameof(EmailProviderKind.Mailjet), MailjetValidation.Validate, s => s.AddPipelineProvider<MailjetMapper>(MailjetMapper.ClientName)),
    ];

    public static ProviderDefinition Get(EmailProviderKind kind)
        => All.FirstOrDefault(d => d.Kind == kind)
           ?? throw new InvalidOperationException($"No provider definition for {kind}.");
}
