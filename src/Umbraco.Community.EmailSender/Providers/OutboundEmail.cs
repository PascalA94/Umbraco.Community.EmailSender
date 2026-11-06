using System.Globalization;
using MimeKit;
using Umbraco.Cms.Core.Models.Email;

namespace Umbraco.Community.EmailSender.Providers;

/// <summary>
/// The provider-neutral shape of one email. <see cref="Create"/> is the only place an <see cref="EmailMessage"/> is
/// interpreted: From fallback, recipient clean-up, body kind, size cap and the Expires header.
/// </summary>
internal sealed record OutboundEmail(
    string From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    IReadOnlyList<string> ReplyTo,
    string Subject,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyList<OutboundAttachment> Attachments,
    IReadOnlyDictionary<string, string> Headers)
{
    private const int CopyBufferSize = 8192;

    /// <exception cref="EmailSendException">No usable From, no recipient, an invalid address or attachments over the cap.</exception>
    public static OutboundEmail Create(
        EmailMessage message,
        string? optionsFrom,
        string? smtpFrom,
        TimeSpan? expires,
        long maxAttachmentBytes,
        TimeProvider clock,
        bool fromRequired = true)
    {
        // Error text never carries an address, subject or file name: addresses are personal data.
        string? from = FirstNonBlank(message.From, optionsFrom, smtpFrom);
        if (from is null && fromRequired)
        {
            throw new EmailSendException("No From address. Set EmailSender:From or Umbraco:CMS:Global:Smtp:From.");
        }

        if (from is null)
        {
            from = string.Empty;
        }
        else if (!TryNormalise(from, keepDisplayName: true, out from))
        {
            throw new EmailSendException("The From address is not a valid email address.");
        }

        var to = Addresses(message.To, "To");
        if (to.Count == 0)
        {
            throw new EmailSendException("The email has no valid recipient in To.");
        }

        var headers = new Dictionary<string, string>();
        if (expires is { } lifetime)
        {
            headers["Expires"] = (clock.GetUtcNow() + lifetime).UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
        }

        return new OutboundEmail(
            from,
            to,
            Addresses(message.Cc, "Cc"),
            Addresses(message.Bcc, "Bcc"),
            Addresses(message.ReplyTo, "ReplyTo"),
            StripControlCharacters(message.Subject ?? string.Empty),
            message.IsBodyHtml ? message.Body : null,
            message.IsBodyHtml ? null : message.Body,
            ReadAttachments(message.Attachments, maxAttachmentBytes),
            headers);
    }

    // Counts only: the generated record ToString would print addresses, subject and body.
    public override string ToString()
        => $"OutboundEmail {{ To = {To.Count}, Cc = {Cc.Count}, Bcc = {Bcc.Count}, Attachments = {Attachments.Count} }}";

    /// <summary>
    /// MimeKit accepts quoted names, comments and encoded words that can carry extra address-like text or CR/LF, so the raw
    /// string is never forwarded: recipients become the bare mailbox address, and a From display name survives only when it
    /// is plain text.
    /// </summary>
    private static bool TryNormalise(string raw, bool keepDisplayName, out string normalised)
    {
        normalised = string.Empty;
        if (raw.Any(char.IsControl) || !MailboxAddress.TryParse(raw.Trim(), out var mailbox))
        {
            return false;
        }

        var address = mailbox.Address;
        if (string.IsNullOrWhiteSpace(address) || address.Any(c => char.IsControl(c) || c is ',' or ';' or '(' or ')' or '<' or '>' or '"' or ' '))
        {
            return false;
        }

        var name = mailbox.Name?.Trim();
        var safeName = !string.IsNullOrEmpty(name) && name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '\'' or '&');
        normalised = keepDisplayName && safeName ? $"{name} <{address}>" : address;
        return true;
    }

    private static string StripControlCharacters(string value)
        => System.Text.RegularExpressions.Regex.Replace(value, @"\p{Cc}+", " ");

    private static string SafeFileName(string? name)
    {
        var cleaned = StripControlCharacters(name ?? string.Empty);
        cleaned = cleaned[(cleaned.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        return cleaned.Length == 0 ? "attachment" : cleaned;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static List<string> Addresses(string?[]? values, string field)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!TryNormalise(value, keepDisplayName: false, out var normalised))
            {
                throw new EmailSendException($"A recipient in {field} is not a valid email address.");
            }

            result.Add(normalised);
        }

        return result;
    }

    private static List<OutboundAttachment> ReadAttachments(IEnumerable<EmailMessageAttachment>? attachments, long cap)
    {
        var list = attachments?.ToList() ?? [];
        var tooLarge = new EmailSendException($"The attachments are larger than EmailSender:MaxTotalAttachmentBytes ({cap} bytes).");

        // Seekable streams are measured first so an oversized email is refused before any byte is read.
        if (list.All(a => a.Stream.CanSeek) && list.Sum(a => a.Stream.Length) > cap)
        {
            throw tooLarge;
        }

        long total = 0;
        var result = new List<OutboundAttachment>(list.Count);
        foreach (var attachment in list)
        {
            // A notification handler may have read the stream without handling the email.
            if (attachment.Stream is { CanSeek: true, Position: > 0 })
            {
                attachment.Stream.Position = 0;
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[CopyBufferSize];
            int read;
            while ((read = attachment.Stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > cap)
                {
                    throw tooLarge;
                }

                buffer.Write(chunk, 0, read);
            }

            var fileName = SafeFileName(attachment.FileName);
            result.Add(new OutboundAttachment(
                fileName,
                MimeTypes.GetMimeType(fileName),
                Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length)));
        }

        return result;
    }
}
