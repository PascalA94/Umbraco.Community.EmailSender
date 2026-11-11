using System.Net;
using System.Text.RegularExpressions;

namespace Umbraco.Community.EmailSender.Providers.GovUkNotify;

/// <summary>
/// Notify renders its own Markdown subset and shows HTML as text, so an HTML body (Umbraco's invite and reset emails are
/// HTML) is reduced to text. Only http and https links survive, as <c>[text](url)</c>. The output is never HTML, so a
/// crude parser cannot become an injection route; the regexes carry a timeout against pathological input.
/// Notify also renders Markdown, so literal square brackets in any other text become round ones: otherwise a content node
/// named <c>[Verify here](https://evil)</c> would reach a trusted sender as a live link.
/// </summary>
internal static partial class NotifyBodyFormatter
{
    /// <summary>A plain-text body: only the link syntax is neutralised.</summary>
    public static string FromText(string text) => NeutraliseBrackets(text);

    public static string ToText(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        // Each stretch of text between anchors is cleaned on its own and the generated links are appended afterwards, so
        // no placeholder ever passes through the entity decode: a visitor's text could otherwise spell one (&#xE000;0&#xE001;).
        var source = Dropped().Replace(html, " ");
        var builder = new System.Text.StringBuilder(source.Length);
        var last = 0;
        foreach (Match anchor in Anchor().Matches(source))
        {
            builder.Append(CleanSegment(source[last..anchor.Index]));
            builder.Append(AnchorToMarkdown(anchor));
            last = anchor.Index + anchor.Length;
        }

        builder.Append(CleanSegment(source[last..]));
        var text = builder.ToString();
        text = Spaces().Replace(text, " ");
        text = SpaceAroundNewline().Replace(text, "\n");
        text = ManyNewlines().Replace(text, "\n\n");
        return text.Trim();
    }

    private static string CleanSegment(string segment)
    {
        var text = LineBreak().Replace(segment, "\n");
        text = Paragraph().Replace(text, "\n\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = ControlChars().Replace(text, " ");
        return NeutraliseBrackets(text);
    }

    private static string NeutraliseBrackets(string text) => text.Replace('[', '(').Replace(']', ')');

    private static string AnchorToMarkdown(Match match)
    {
        var url = WebUtility.HtmlDecode(match.Groups["u"].Value.Trim());
        var label = WebUtility.HtmlDecode(AnyTag().Replace(match.Groups["t"].Value, string.Empty)).Trim();
        label = label.Replace("[", string.Empty).Replace("]", string.Empty);

        var safe = (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && !url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
        if (!safe)
        {
            return label;
        }

        // Parentheses would end the Markdown link early.
        url = url.Replace("(", "%28").Replace(")", "%29");
        return label.Length == 0 || label == url ? url : $"[{label}]({url})";
    }

    [GeneratedRegex(@"<!--.*?-->|<(head|style|script)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Dropped();

    [GeneratedRegex(@"<a\b[^>]*?\bhref\s*=\s*(?:""(?<u>[^""]*)""|'(?<u>[^']*)')[^>]*>(?<t>.*?)</a\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|tr|li|h[1-6]|table|ul|ol)\s*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Paragraph();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 1000)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[^\P{Cc}\n]+", RegexOptions.None, 1000)]
    private static partial Regex ControlChars();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.None, 1000)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[ \t]*\n[ \t]*", RegexOptions.None, 1000)]
    private static partial Regex SpaceAroundNewline();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, 1000)]
    private static partial Regex ManyNewlines();
}
