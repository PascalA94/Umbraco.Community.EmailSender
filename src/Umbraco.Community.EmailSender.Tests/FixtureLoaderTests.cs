using System.Text.Json;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>
/// The provider fixtures are copied from the private research workspace (design 9.5). This guards that every folder made it
/// into the test output with its provenance note and a basic request, and that no JSON file was damaged by the scrub.
/// </summary>
public class FixtureLoaderTests
{
    private static readonly string[] Names =
    [
        "postmark", "resend", "sendgrid", "mailgun", "brevo", "mailjet", "bird",
        "mailersend", "smtp2go", "elasticemail", "mandrill", "mailtrap", "graph",
    ];

    public static TheoryData<string> Folders() => new(Names);

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryProviderFolderLoads(string folder)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", folder);

        Assert.True(Directory.Exists(path), $"Fixtures/{folder} is not in the test output.");
        Assert.True(File.Exists(Path.Combine(path, "SOURCE.md")), $"Fixtures/{folder} has no SOURCE.md.");
        Assert.NotEmpty(Directory.GetFiles(path, "request-basic.*"));
        foreach (var file in Directory.GetFiles(path, "*.json"))
        {
            using var _ = JsonDocument.Parse(File.ReadAllBytes(file));
        }
    }

    [Fact]
    public void NoFolderBeyondTheThirteen()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        Assert.Equal(Names.Order(), Directory.GetDirectories(root).Select(d => Path.GetFileName(d)).Order());
    }
}
