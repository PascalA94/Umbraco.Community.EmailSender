using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Community.EmailSender.Providers;

namespace Umbraco.Community.EmailSender.Tests;

public class OutboundEmailTests
{
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    private const long Cap = 1024;

    private static EmailMessage Message(
        string? from = "from@example.test",
        string?[]? to = null,
        string body = "hello",
        bool html = false,
        IList<EmailMessageAttachment>? attachments = null)
        => new(from!, (to ?? ["to@example.test"])!, null!, null!, null!, "Subject text", body, html, attachments);

    private static OutboundEmail Create(
        EmailMessage message,
        string? optionsFrom = null,
        string? smtpFrom = null,
        TimeSpan? expires = null,
        long cap = Cap)
        => OutboundEmail.Create(message, optionsFrom, smtpFrom, expires, cap, Clock);

    [Theory]
    [InlineData("msg@example.test", "opt@example.test", "smtp@example.test", "msg@example.test")]
    [InlineData("", "opt@example.test", "smtp@example.test", "opt@example.test")]
    [InlineData(null, "opt@example.test", "smtp@example.test", "opt@example.test")]
    [InlineData("  ", null, "smtp@example.test", "smtp@example.test")]
    [InlineData(null, " ", "smtp@example.test", "smtp@example.test")]
    public void From_FallbackOrder(string? message, string? options, string? smtp, string expected)
        => Assert.Equal(expected, Create(Message(from: message), options, smtp).From);

    [Fact]
    public void From_NoneConfigured_NamesBothKeys()
    {
        var ex = Assert.Throws<EmailSendException>(() => Create(Message(from: null)));

        Assert.Equal("No From address. Set EmailSender:From or Umbraco:CMS:Global:Smtp:From.", ex.Message);
    }

    [Fact]
    public void From_Invalid_ThrowsWithoutEchoingTheValue()
    {
        var ex = Assert.Throws<EmailSendException>(() => Create(Message(from: "not an address <<")));

        Assert.DoesNotContain("not an address", ex.Message);
    }

    [Fact]
    public void Recipients_NullsAndBlanksDropped()
    {
        var email = Create(Message(to: ["a@example.test", null, "", "  ", "b@example.test"]));

        Assert.Equal(["a@example.test", "b@example.test"], email.To);
        Assert.Empty(email.Cc);
        Assert.Empty(email.Bcc);
        Assert.Empty(email.ReplyTo);
    }

    [Fact]
    public void CcBccReplyTo_AreMapped()
    {
        var message = new EmailMessage(
            "from@example.test", ["to@example.test"], ["cc@example.test"], ["bcc@example.test"], ["reply@example.test"],
            "s", "b", false, null);

        var email = Create(message);

        Assert.Equal(["cc@example.test"], email.Cc);
        Assert.Equal(["bcc@example.test"], email.Bcc);
        Assert.Equal(["reply@example.test"], email.ReplyTo);
    }

    [Fact]
    public void NoRecipients_Throws()
    {
        // EmailMessage's constructor only refuses null or empty strings, so whitespace gets through to us.
        var ex = Assert.Throws<EmailSendException>(() => Create(Message(to: ["  "])));

        Assert.Contains("recipient", ex.Message);
    }

    [Fact]
    public void InvalidRecipient_ThrowsWithoutEchoingTheAddress()
    {
        var ex = Assert.Throws<EmailSendException>(() => Create(Message(to: ["bad address <<"])));

        Assert.DoesNotContain("bad address", ex.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsBodyHtml_PicksHtmlOrText(bool html)
    {
        var email = Create(Message(body: "the body", html: html));

        Assert.Equal(html ? "the body" : null, email.HtmlBody);
        Assert.Equal(html ? null : "the body", email.TextBody);
    }

    [Fact]
    public void Expires_AddsRfc1123Header()
    {
        var email = Create(Message(), expires: TimeSpan.FromHours(1));

        Assert.Equal("Wed, 04 Nov 2026 11:00:00 GMT", email.Headers["Expires"]);
    }

    [Fact]
    public void NoExpiry_NoHeader() => Assert.Empty(Create(Message()).Headers);

    [Fact]
    public void Attachments_AreBase64Encoded()
    {
        var bytes = "payload"u8.ToArray();
        var email = Create(Message(attachments: [new EmailMessageAttachment(new MemoryStream(bytes), "Café menu.pdf")]));

        var attachment = Assert.Single(email.Attachments);
        Assert.Equal("Café menu.pdf", attachment.Name);
        Assert.Equal(Convert.ToBase64String(bytes), attachment.Base64);
    }

    [Fact]
    public void Attachments_OverCap_ThrowsBeforeReadingAll()
    {
        var first = new CountingStream(new byte[800]);
        var second = new CountingStream(new byte[800]);
        var message = Message(attachments: [new(first, "a.bin"), new(second, "b.bin")]);

        var ex = Assert.Throws<EmailSendException>(() => Create(message));

        Assert.Contains("attachments", ex.Message);
        Assert.Equal(0, first.BytesRead + second.BytesRead);
    }

    [Fact]
    public void Attachments_NonSeekableOverCap_StopsReadingAtTheCap()
    {
        var huge = new NonSeekableStream(new byte[100_000]);
        var message = Message(attachments: [new(huge, "a.bin")]);

        Assert.Throws<EmailSendException>(() => Create(message));

        Assert.True(huge.BytesRead <= Cap + 8192, $"read {huge.BytesRead} bytes");
    }

    [Theory]
    [InlineData("menu.pdf", "application/pdf")]
    [InlineData("photo.PNG", "image/png")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("data.unknownext", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void ContentType_FromFileExtension(string fileName, string expected)
    {
        var email = Create(Message(attachments: [new(new MemoryStream([1]), fileName)]));

        Assert.Equal(expected, email.Attachments.Single().ContentType);
    }

    [Theory]
    [InlineData("\"Victim, evil@evil.test\" <a@b.test>")]
    [InlineData("a@b.test (x, evil@evil.test)")]
    [InlineData("=?utf-8?b?ZXZpbEBldmlsLnRlc3Q=?= <a@b.test>")]
    public void Recipients_AreForwardedAsBareAddresses_NeverTheRawString(string raw)
    {
        var email = Create(Message(to: [raw]));

        var forwarded = Assert.Single(email.To);
        Assert.DoesNotContain("evil", forwarded);
        Assert.DoesNotContain("\r", forwarded);
        Assert.DoesNotContain("\n", forwarded);
        Assert.DoesNotContain(",", forwarded);
        Assert.DoesNotContain("(", forwarded);
    }

    [Theory]
    [InlineData("a(comment\r\nX-Inj: 1)@b.com")]
    [InlineData("\"x\r\ny\" <a@b.com>")]
    public void Recipients_WithControlCharacters_AreRejected(string raw)
    {
        var ex = Assert.Throws<EmailSendException>(() => Create(Message(to: [raw])));

        Assert.DoesNotContain("X-Inj", ex.Message);
    }

    [Fact]
    public void From_KeepsASafeDisplayName_AndDropsAnUnsafeOne()
    {
        Assert.Equal("Site Team <from@example.test>", Create(Message(from: "Site Team <from@example.test>")).From);
        Assert.Equal("from@example.test", Create(Message(from: "\"Evil, x@y.test\" <from@example.test>")).From);
    }

    [Fact]
    public void ControlCharactersInSubject_AreReplaced()
    {
        var message = new EmailMessage("from@example.test", ["to@example.test"], null!, null!, null!, "Hi\r\nBcc: x@y.test", "b", false, null);

        Assert.DoesNotContain("\n", Create(message).Subject);
        Assert.DoesNotContain("\r", Create(message).Subject);
    }

    [Theory]
    [InlineData("a\r\nb.pdf", "a b.pdf")]
    [InlineData("..\\..\\etc\\passwd.txt", "passwd.txt")]
    [InlineData("/var/x/report.pdf", "report.pdf")]
    [InlineData("", "attachment")]
    [InlineData("   ", "attachment")]
    public void AttachmentName_IsSanitised(string name, string expected)
    {
        var email = Create(Message(attachments: [new(new MemoryStream([1]), name)]));

        Assert.Equal(expected, email.Attachments.Single().Name);
    }

    [Fact]
    public void Attachments_SeekableStreamPartlyRead_IsReadFromTheStart()
    {
        var stream = new MemoryStream("payload"u8.ToArray());
        stream.Position = stream.Length; // a notification handler consumed it without handling the email

        var email = Create(Message(attachments: [new(stream, "a.txt")]));

        Assert.Equal(Convert.ToBase64String("payload"u8.ToArray()), email.Attachments.Single().Base64);
    }

    [Fact]
    public void ToString_OmitsContent()
    {
        var email = Create(
            Message(to: ["secret-person@example.test"], body: "secret body", attachments: [new(new MemoryStream([1]), "secret.pdf")]),
            expires: TimeSpan.FromHours(1));

        var text = email + " " + email.Attachments.Single();

        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("Subject text", text);
        Assert.DoesNotContain("from@example.test", text);
    }

    private class CountingStream(byte[] data) : MemoryStream(data)
    {
        public int BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }
    }

    private sealed class NonSeekableStream(byte[] data) : CountingStream(data)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
