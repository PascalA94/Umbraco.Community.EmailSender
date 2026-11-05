using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;
using Umbraco.Community.EmailSender.Providers.Postmark;
using Umbraco.Extensions;

namespace Umbraco.Community.EmailSender.Web;

/// <summary>The package's only composer. Registers nothing unless <c>EmailSender:Provider</c> is set.</summary>
public sealed class EmailSenderComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var provider = builder.Config.GetSection(EmailSenderOptions.SectionName)[nameof(EmailSenderOptions.Provider)];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return;
        }

        // Fails at start-up with the allowed values, before any service is registered.
        var kind = EmailProviderKindParser.Parse(provider);

        builder.Services.AddOptions<EmailSenderOptions>()
            .BindConfiguration(EmailSenderOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EmailSenderOptions>, EmailSenderOptionsValidator>();

        // Both are replaced: core's EmailSender returns before calling the client unless Smtp:Host or a pickup
        // folder is set (design section 0 and 4.1).
        builder.Services.AddUnique<IEmailSenderClient, HttpApiEmailSenderClient>();
        builder.Services.AddUnique<IEmailSender, HttpApiEmailSender>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // The provider is chosen once here: a Provider change needs a restart (it also decides which services exist).
        switch (kind)
        {
            case EmailProviderKind.Postmark:
                builder.Services.AddPostmarkProvider();
                break;
            case EmailProviderKind.AzureCommunicationServices:
                builder.Services.AddAcsProvider();
                break;
        }

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, SmtpOverlapStartupWarning>();
    }
}
