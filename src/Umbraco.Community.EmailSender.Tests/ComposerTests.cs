using Microsoft.Extensions.Configuration;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Infrastructure.Mail.Interfaces;
using Umbraco.Cms.Infrastructure.DependencyInjection;
using Umbraco.Community.EmailSender.Web;

namespace Umbraco.Community.EmailSender.Tests;

public class ComposerTests
{
    private static ServiceCollection Compose(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new UmbracoBuilder(services, config, new TypeLoader(
            Substitute.For<ITypeFinder>(), Substitute.For<ILogger<TypeLoader>>()));
        // Core registers IEmailSender and IEmailSenderClient in these two steps; run them so the tests prove our
        // registrations win over the real ones.
        typeof(UmbracoBuilder).GetMethod("AddCoreServices", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(builder, null);
        builder.AddCoreInitialServices();
        new EmailSenderComposer().Compose(builder);
        return services;
    }

    private static Type? ImplementationOf<T>(ServiceCollection services)
        => services.Last(d => d.ServiceType == typeof(T)).ImplementationType;

    [Fact]
    public void NoProvider_CoreServicesRemain()
    {
        var services = Compose([]);

        Assert.Equal(typeof(Umbraco.Cms.Infrastructure.Mail.BasicSmtpEmailSenderClient), ImplementationOf<IEmailSenderClient>(services));
        Assert.Null(ImplementationOf<IEmailSender>(services)); // core registers IEmailSender by factory
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(HttpApiEmailSender));
    }

    [Fact]
    public void Postmark_ReplacesBoth()
    {
        var services = Compose(new()
        {
            ["EmailSender:Provider"] = "Postmark",
            ["EmailSender:Postmark:ServerToken"] = "pm-SENTINEL", // gitleaks:allow
        });

        Assert.Equal(typeof(HttpApiEmailSender), ImplementationOf<IEmailSender>(services));
        Assert.Equal(typeof(HttpApiEmailSenderClient), ImplementationOf<IEmailSenderClient>(services));
    }

    [Fact]
    public void UnknownProvider_FailsAtComposeNamingAllowedValues()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Compose(new() { ["EmailSender:Provider"] = "Mailgun" }));

        Assert.Contains("Postmark", ex.Message);
    }

    [Fact]
    public void Postmark_WiresBindingAndStartupValidation()
    {
        var services = Compose(new()
        {
            ["EmailSender:Provider"] = "Postmark",
            ["EmailSender:Postmark:ServerToken"] = "pm-SENTINEL", // gitleaks:allow
            ["EmailSender:TimeoutSeconds"] = "7",
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailSender:Provider"] = "Postmark",
            ["EmailSender:Postmark:ServerToken"] = "pm-SENTINEL", // gitleaks:allow
            ["EmailSender:TimeoutSeconds"] = "7",
        }).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Options.IValidateOptions<Configuration.EmailSenderOptions>>(),
            v => v is Configuration.EmailSenderOptionsValidator);
        Assert.Equal(7, provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Configuration.EmailSenderOptions>>().CurrentValue.TimeoutSeconds);
    }

    [Fact]
    public void Postmark_MissingToken_FailsWhenOptionsAreRead()
    {
        var settings = new Dictionary<string, string?> { ["EmailSender:Provider"] = "Postmark" };
        var services = Compose(settings);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.EmailSenderOptions>>().Value);

        Assert.Contains("EmailSender:Postmark:ServerToken", ex.Message);
    }

    [Fact]
    public void Postmark_RegistersStartupWarningHandler()
    {
        var services = Compose(new() { ["EmailSender:Provider"] = "Postmark", ["EmailSender:Postmark:ServerToken"] = "x" }); // gitleaks:allow

        Assert.Contains(services, d => d.ImplementationType == typeof(SmtpOverlapStartupWarning));
    }

    [Theory]
    [InlineData("Postmark", "EmailSender:Postmark:ServerToken", typeof(Umbraco.Community.EmailSender.Providers.Postmark.PostmarkProvider))]
    [InlineData("AzureCommunicationServices", "EmailSender:AzureCommunicationServices:ConnectionString", typeof(Umbraco.Community.EmailSender.Providers.AzureCommunicationServices.AcsProvider))]
    [InlineData("GovUkNotify", "EmailSender:GovUkNotify:ApiKey", typeof(Umbraco.Community.EmailSender.Providers.GovUkNotify.NotifyProvider))]
    public void Provider_ResolvesSenderAndProvider(string kind, string secretKey, Type expectedProvider)
    {
        var settings = new Dictionary<string, string?> { ["EmailSender:Provider"] = kind, [secretKey] = "x" }; // gitleaks:allow
        var services = Compose(settings);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton(Substitute.For<Umbraco.Cms.Core.Events.IEventAggregator>());
        services.AddOptions<Umbraco.Cms.Core.Configuration.Models.GlobalSettings>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });

        Assert.IsType<HttpApiEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.IsType(expectedProvider, provider.GetRequiredService<Umbraco.Community.EmailSender.Providers.IEmailProvider>());
        Assert.Same(
            provider.GetRequiredService<Umbraco.Community.EmailSender.Providers.IEmailProvider>(),
            provider.GetRequiredService<Umbraco.Community.EmailSender.Providers.IEmailProvider>());
    }
}
