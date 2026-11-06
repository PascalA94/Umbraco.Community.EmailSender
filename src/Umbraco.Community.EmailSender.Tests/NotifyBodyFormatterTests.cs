using Umbraco.Community.EmailSender.Providers.GovUkNotify;

namespace Umbraco.Community.EmailSender.Tests;

public class NotifyBodyFormatterTests
{
    // Entity-encoded private-use characters decode to the formatter's own link markers if the decode runs
    // over text that still holds markers.
    [Theory]
    [InlineData("&#xE000;0&#xE001;")]
    [InlineData("&#xE000;99&#xE001;")]
    [InlineData("&#xE000;99999999999999999999&#xE001;")]
    public void EncodedLinkMarkers_AreNotTreatedAsLinks(string visitorText)
    {
        var html = $"<p>{visitorText}</p><a href=\"https://site.example/reset?t=abc\">reset</a>";

        var text = NotifyBodyFormatter.ToText(html);

        Assert.Equal(1, CountOccurrences(text, "https://site.example/reset?t=abc"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
