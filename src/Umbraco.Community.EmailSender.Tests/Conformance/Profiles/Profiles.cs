namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal static class Profiles
{
    public static IReadOnlyList<IProviderProfile> All { get; } =
    [
        new AcsProfile(),
        new PostmarkProfile(),
        new NotifyProfile(),
    ];

    public static IProviderProfile Get(string kind) => All.Single(p => p.Kind.ToString() == kind);
}
