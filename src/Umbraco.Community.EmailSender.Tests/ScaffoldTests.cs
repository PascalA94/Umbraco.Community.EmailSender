using System.Reflection;

namespace Umbraco.Community.EmailSender.Tests;

public class ScaffoldTests
{
    private static string Major(Assembly assembly)
        => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "UmbracoMajor").Value!;

    [Fact]
    public void Assembly_TargetsSelectedMajor()
    {
        var selected = Major(typeof(ScaffoldTests).Assembly);
        Assert.Equal(selected, Major(typeof(Web.EmailSenderComposer).Assembly));
        Assert.Equal(selected, typeof(Umbraco.Cms.Core.Constants).Assembly.GetName().Version!.Major.ToString());
    }
}
