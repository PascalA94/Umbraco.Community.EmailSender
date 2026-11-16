using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Registry;

namespace Umbraco.Community.EmailSender.Tests;

public class RegistryTests
{
    public static TheoryData<EmailProviderKind> Kinds() => new(Enum.GetValues<EmailProviderKind>());

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindHasDefinition(EmailProviderKind kind)
    {
        var definition = ProviderRegistry.Get(kind);

        Assert.Equal(kind, definition.Kind);
        Assert.Single(ProviderRegistry.All, d => d.Kind == kind);
    }

    [Fact]
    public void AllCoversEveryKindOnce()
        => Assert.Equal(Enum.GetValues<EmailProviderKind>().Order(), ProviderRegistry.All.Select(d => d.Kind).Order());

    // The section name is the Provider value, and both are permanent config keys (design 9.3).
    [Theory]
    [MemberData(nameof(Kinds))]
    public void SectionEqualsKindName(EmailProviderKind kind)
    {
        var definition = ProviderRegistry.Get(kind);

        Assert.Equal(kind.ToString(), definition.Section);
        Assert.NotNull(typeof(EmailSenderOptions).GetProperty(definition.Section));
    }

    // Design 9.5: no provider may skip the conformance suite or the leak sweep.
    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKindHasDefinitionAndProfile(EmailProviderKind kind)
    {
        Assert.Equal(kind, ProviderRegistry.Get(kind).Kind);
        Assert.Single(Conformance.Profiles.All, p => p.Kind == kind);
    }
}
