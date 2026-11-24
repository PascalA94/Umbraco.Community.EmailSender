using System.Text.RegularExpressions;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>
/// The README is the only place a user learns what each provider has proven (design 9.7). These guard that every provider has a
/// guide and one of the three labels (Experimental is added on top of one), that "supported" is never claimed beyond a label, and that the prerelease package does not
/// advertise itself to the Marketplace (design 9.12).
/// </summary>
public class ReadmeTests
{
    private static readonly string[] Labels =
    [
        "Verified live",
        "Built from the provider's docs and checked against recorded examples",
        "Unverified",
    ];

    private static readonly Dictionary<EmailProviderKind, string> Headings = new()
    {
        [EmailProviderKind.AzureCommunicationServices] = "Azure Communication Services",
        [EmailProviderKind.Postmark] = "Postmark",
        [EmailProviderKind.GovUkNotify] = "GOV.UK Notify",
        [EmailProviderKind.SendGrid] = "SendGrid",
        [EmailProviderKind.Resend] = "Resend",
        [EmailProviderKind.Mailgun] = "Mailgun",
        [EmailProviderKind.Brevo] = "Brevo",
        [EmailProviderKind.Mailjet] = "Mailjet",
        [EmailProviderKind.Bird] = "Bird",
        [EmailProviderKind.MailerSend] = "MailerSend",
        [EmailProviderKind.Smtp2Go] = "SMTP2GO",
        [EmailProviderKind.ElasticEmail] = "Elastic Email",
        [EmailProviderKind.Mandrill] = "Mandrill",
        [EmailProviderKind.Mailtrap] = "Mailtrap",
        [EmailProviderKind.MicrosoftGraph] = "Microsoft Graph",
    };

    public static TheoryData<EmailProviderKind> Kinds() => new(Enum.GetValues<EmailProviderKind>());

    [Fact]
    public void HeadingTable_CoversEveryKind()
        => Assert.Equal(Enum.GetValues<EmailProviderKind>().Length, Headings.Count);

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryProviderHasGuideAndLabel(EmailProviderKind kind)
    {
        var readme = File.ReadAllText(RepoFile("README.md"));
        var heading = Headings[kind];

        Assert.Matches(new Regex($"^### {Regex.Escape(heading)}\\s*$", RegexOptions.Multiline), readme);

        var row = readme.Split('\n').FirstOrDefault(l => l.StartsWith('|') && l.Contains($"`{kind}`", StringComparison.Ordinal));
        Assert.True(row is not null, $"The status table has no row for `{kind}`.");
        Assert.True(Labels.Any(l => row!.Contains(l, StringComparison.Ordinal)), $"The `{kind}` row carries none of the three labels (Experimental is added on top of one).");
    }

    [Fact]
    public void NotifyIsLabelledExperimental()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));
        var row = readme.Split('\n').First(l => l.StartsWith('|') && l.Contains("`GovUkNotify`", StringComparison.Ordinal));

        Assert.Contains("Experimental", row);
    }

    [Fact]
    public void HealthCheckSection_DoesNotClaimEveryProviderChecksCredentials()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));
        var start = readme.IndexOf("## Health check", StringComparison.Ordinal);
        var section = readme[start..readme.IndexOf("## Security notes", start, StringComparison.Ordinal)];

        // Only Postmark, GOV.UK Notify and Graph prove the credentials; the other providers report Info.
        Assert.DoesNotContain("Most providers", section);
        Assert.Contains("Info", section);
        Assert.Contains("Microsoft Graph", section);
    }

    [Fact]
    public void RetryText_NamesTheGraph401Refresh()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));

        Assert.Contains("repeats a 401 once", readme);
    }

    [Fact]
    public void NoSupportedClaimBeyondLabel()
    {
        var readme = File.ReadAllText(RepoFile("README.md"));

        foreach (Match m in Regex.Matches(readme, @"\bsupported\b", RegexOptions.IgnoreCase))
        {
            var before = readme[Math.Max(0, m.Index - 12)..m.Index].ToLowerInvariant();
            var allowed = before.EndsWith("not ") || before.EndsWith("or ") /* "made or supported by Umbraco HQ" */;
            Assert.True(allowed, $"README claims \"supported\" at offset {m.Index}: ...{readme[Math.Max(0, m.Index - 40)..Math.Min(readme.Length, m.Index + 30)]}...");
        }
    }

    [Fact]
    public void PackageTags_HaveNoMarketplaceTagWhilePrerelease()
    {
        var props = File.ReadAllText(RepoFile("Directory.Build.props"));
        var prerelease = Regex.IsMatch(props, @"<PackageSuffix>\s*\S+\s*</PackageSuffix>");
        var csproj = File.ReadAllText(RepoFile("src/Umbraco.Community.EmailSender/Umbraco.Community.EmailSender.csproj"));
        var tags = Regex.Match(csproj, "<PackageTags>(.*?)</PackageTags>").Groups[1].Value.Split(';');

        Assert.Contains("umbraco", tags);
        if (prerelease)
        {
            Assert.DoesNotContain("umbraco-marketplace", tags);
        }

        foreach (var tag in new[] { "sendgrid", "resend", "mailgun", "brevo", "mailjet", "microsoft-graph" })
        {
            Assert.Contains(tag, tags);
        }
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }
}
