namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal static class Profiles
{
    public static IReadOnlyList<IProviderProfile> All { get; } =
    [
        new AcsProfile(),
        new PostmarkProfile(),
        new NotifyProfile(),
        new SendGridProfile(),
        new ResendProfile(),
        new MailgunProfile(),
        new BrevoProfile(),
        new MailjetProfile(),
        new BirdProfile(),
        new MailerSendProfile(),
        new Smtp2GoProfile(),
        new ElasticEmailProfile(),
        new MandrillProfile(),
        new MailtrapProfile(),
        new MicrosoftGraphProfile(),
    ];

    public static IProviderProfile Get(string kind) => All.Single(p => p.Kind.ToString() == kind);
}
