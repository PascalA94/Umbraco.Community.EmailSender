using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.Bird;
using Umbraco.Community.EmailSender.Providers.Brevo;
using Umbraco.Community.EmailSender.Providers.ElasticEmail;
using Umbraco.Community.EmailSender.Providers.MailerSend;
using Umbraco.Community.EmailSender.Providers.Mailgun;
using Umbraco.Community.EmailSender.Providers.Mailtrap;
using Umbraco.Community.EmailSender.Providers.Mandrill;
using Umbraco.Community.EmailSender.Providers.MicrosoftGraph;
using Umbraco.Community.EmailSender.Providers.Mailjet;
using Umbraco.Community.EmailSender.Providers.Pipeline;
using Umbraco.Community.EmailSender.Providers.GovUkNotify;
using Umbraco.Community.EmailSender.Providers.Postmark;
using Umbraco.Community.EmailSender.Providers.Resend;
using Umbraco.Community.EmailSender.Providers.SendGrid;
using Umbraco.Community.EmailSender.Providers.Smtp2Go;

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
        new(EmailProviderKind.Bird, nameof(EmailProviderKind.Bird), BirdValidation.Validate, s => s.AddPipelineProvider<BirdMapper>(BirdMapper.ClientName)),
        new(EmailProviderKind.MailerSend, nameof(EmailProviderKind.MailerSend), MailerSendValidation.Validate, s => s.AddPipelineProvider<MailerSendMapper>(MailerSendMapper.ClientName)),
        new(EmailProviderKind.Smtp2Go, nameof(EmailProviderKind.Smtp2Go), Smtp2GoValidation.Validate, s => s.AddPipelineProvider<Smtp2GoMapper>(Smtp2GoMapper.ClientName)),
        new(EmailProviderKind.ElasticEmail, nameof(EmailProviderKind.ElasticEmail), ElasticEmailValidation.Validate, s => s.AddPipelineProvider<ElasticEmailMapper>(ElasticEmailMapper.ClientName)),
        new(EmailProviderKind.Mandrill, nameof(EmailProviderKind.Mandrill), MandrillValidation.Validate, s => s.AddPipelineProvider<MandrillMapper>(MandrillMapper.ClientName)),
        new(EmailProviderKind.Mailtrap, nameof(EmailProviderKind.Mailtrap), MailtrapValidation.Validate, s => s.AddPipelineProvider<MailtrapMapper>(MailtrapMapper.ClientName)),
        new(EmailProviderKind.MicrosoftGraph, nameof(EmailProviderKind.MicrosoftGraph), MicrosoftGraphValidation.Validate, s => s.AddMicrosoftGraphProvider()),
    ];

    public static ProviderDefinition Get(EmailProviderKind kind)
        => All.FirstOrDefault(d => d.Kind == kind)
           ?? throw new InvalidOperationException($"No provider definition for {kind}.");
}
