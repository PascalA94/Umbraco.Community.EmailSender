using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;
using Umbraco.Community.EmailSender.Web;

namespace Umbraco.Community.EmailSender.IntegrationTests;

/// <summary>
/// Booted-host proof of the package's main claim (design 4.1): with a provider set, core's sender and client are both
/// replaced even though no <c>Smtp:Host</c> exists, so invites are still offered; with none set, core is untouched.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class HostWiringNoProviderTests : UmbracoIntegrationTest
{
    protected override void CustomTestSetup(IUmbracoBuilder builder) => new EmailSenderComposer().Compose(builder);

    [Test]
    public void NoProvider_CoreSenderAndClientAreResolved()
    {
        Assert.That(GetRequiredService<IEmailSender>(), Is.Not.InstanceOf<HttpApiEmailSender>());
        Assert.That(GetRequiredService<IEmailSenderClient>(), Is.Not.InstanceOf<HttpApiEmailSenderClient>());
    }
}

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class HostWiringPostmarkTests : UmbracoIntegrationTest
{
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.Config["EmailSender:Provider"] = "Postmark";
        builder.Config["EmailSender:Postmark:ServerToken"] = "pm-SENTINEL"; // gitleaks:allow
        new EmailSenderComposer().Compose(builder);
    }

    [Test]
    public void Provider_ReplacesSenderAndClient_AndInvitesAreOfferedWithoutSmtp()
    {
        Assert.That(GetRequiredService<IEmailSender>(), Is.InstanceOf<HttpApiEmailSender>());
        Assert.That(GetRequiredService<IEmailSenderClient>(), Is.InstanceOf<HttpApiEmailSenderClient>());
        Assert.That(GetRequiredService<IEmailSender>().CanSendRequiredEmail(), Is.True);
    }
}
