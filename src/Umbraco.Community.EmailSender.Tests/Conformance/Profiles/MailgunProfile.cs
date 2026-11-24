using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

internal sealed class MailgunProfile : IProviderProfile
{
    private const string Key = "key-SENTINEL-KEY"; // gitleaks:allow

    public EmailProviderKind Kind => EmailProviderKind.Mailgun;

    public string FixtureFolder => "mailgun";

    // The key travels only inside Basic credentials, so the encoded form is a secret too: logging the Authorization header
    // would leak it, and the credential-location check finds it there.
    public IReadOnlyList<string> Secrets => [Key, Convert.ToBase64String(Encoding.ASCII.GetBytes("api:" + Key))];

    public EmailSenderOptions Options() => new() { Provider = EmailProviderKind.Mailgun, Mailgun = { ApiKey = Key, Domain = "mg.mailer.test" } };

    public ProfileExpectations Expect { get; } = new()
    {
        CredentialLocations = ["header:Authorization"],
        RequiredPaths = ["from", "to", "subject"],
        // Cc, Bcc, headers and tracking options are not in the curl fixtures (design 9.2).
        ShapeExtras = ["cc", "bcc", "h:", "o:"],
        ReplyTo = ReplyToStyle.FormPart,
        Headers = HeaderPolicy.SendAsGiven,
        Tracking = TrackingStyle.PerRequest,
        Idempotency = IdempotencyCarriage.None,
        Inline = InlineContentId.FileNameOnly,
        MessageId = "message-id@your-domain.com",
        // Mailgun's error bodies are undocumented and the research found no fixture: status-only mapping (design 9.5).
        Auth = new(HttpStatusCode.Unauthorized, string.Empty, "Mailgun rejected the email: HTTP 401. Check EmailSender:Mailgun:ApiKey, and that EmailSender:Mailgun:Region matches the domain's region."),
        Validation = new(HttpStatusCode.BadRequest, string.Empty, "Mailgun rejected the email: HTTP 400."),
        MaxRequestBytes = 25 * 1024 * 1024,
        BinaryAttachments = true,
    };

    public IReadOnlyDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>
    {
        ["F3"] = "Mailgun's response to an unverified sender or an unauthorised sandbox recipient is undocumented [unknown, carried, docs/spikes.md].",
        ["F6"] = "Mailgun documents no quota 429; 420 and 429 are both plain rate limits.",
        ["F8"] = "Mailgun hides no failure in a 2xx: Queued means accepted (design 9.2 body-parse table).",
    };

    public RecordedEmail Normalise(HttpRequestMessage request, byte[] body)
    {
        var parts = Parse(body);
        string? Text(string name) => parts.Where(p => p.Name == name).Select(p => Encoding.UTF8.GetString(p.Data)).FirstOrDefault();
        string[] All(string name) => [.. parts.Where(p => p.Name == name).Select(p => Encoding.UTF8.GetString(p.Data))];

        var from = Text("from") ?? string.Empty;
        var open = from.LastIndexOf(" <", StringComparison.Ordinal);
        var tracking = new[] { "o:tracking", "o:tracking-clicks", "o:tracking-opens" }.Select(Text).ToArray();
        return new RecordedEmail
        {
            FromAddress = open > 0 ? from[(open + 2)..^1] : from,
            FromName = open > 0 ? from[..open] : null,
            To = All("to"),
            Cc = All("cc"),
            Bcc = All("bcc"),
            ReplyTo = Text("h:Reply-To")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            Subject = Text("subject"),
            Html = Text("html"),
            Text = Text("text"),
            Headers = parts.Where(p => p.Name.StartsWith("h:", StringComparison.Ordinal) && p.Name != "h:Reply-To")
                .ToDictionary(p => p.Name[2..], p => Encoding.UTF8.GetString(p.Data)),
            Attachments = [.. parts.Where(p => p.Name is "attachment" or "inline")
                .Select(p => new RecordedAttachment(p.FileName!, p.ContentType!, Convert.ToBase64String(p.Data), p.Name == "inline"))],
            TrackingOff = tracking.All(t => t == "no") ? true : tracking.All(t => t is null) ? null : false,
            PartNames = [.. parts.Select(p => p.Name).Distinct()],
        };
    }

    internal sealed record Part(string Name, string? FileName, string? ContentType, byte[] Data);

    /// <summary>A small byte-level multipart reader: Latin-1 keeps one char per byte, so string indexes are body offsets.</summary>
    internal static List<Part> Parse(byte[] body)
    {
        var raw = Encoding.Latin1.GetString(body);
        var delimiter = raw[..raw.IndexOf("\r\n", StringComparison.Ordinal)];
        var parts = new List<Part>();
        var pos = delimiter.Length + 2;
        while (true)
        {
            var headerEnd = raw.IndexOf("\r\n\r\n", pos, StringComparison.Ordinal);
            var headers = Encoding.UTF8.GetString(body, pos, headerEnd - pos);
            var dataStart = headerEnd + 4;
            var next = raw.IndexOf("\r\n" + delimiter, dataStart, StringComparison.Ordinal);
            // filename* (RFC 5987) wins over filename, as in any server that reads it.
            var star = Regex.Match(headers, @"filename\*=utf-8''([^;\r\n]+)", RegexOptions.IgnoreCase);
            parts.Add(new Part(
                Param(headers, "name") ?? string.Empty,
                star.Success ? Uri.UnescapeDataString(star.Groups[1].Value) : Param(headers, "filename"),
                Regex.Match(headers, @"Content-Type: ([^\r\n]+)", RegexOptions.IgnoreCase) is { Success: true } type ? type.Groups[1].Value : null,
                body[dataStart..next]));
            pos = next + 2 + delimiter.Length;
            if (raw.AsSpan(pos).StartsWith("--"))
            {
                return parts;
            }

            pos += 2;
        }
    }

    // .NET quotes a parameter only when its value is not a plain token, so both forms occur.
    private static string? Param(string headers, string name)
        => Regex.Match(headers, "(?<![\\w*])" + name + "=(?:\"([^\"]*)\"|([^;\\r\\n]+))") is { Success: true } m
            ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            : null;
}
