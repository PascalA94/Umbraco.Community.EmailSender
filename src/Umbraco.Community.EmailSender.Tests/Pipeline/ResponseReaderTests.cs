using System.Net;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Pipeline;

public class ResponseReaderTests
{
    [Fact]
    public async Task Read_CappedAt64Kb()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024 * 1024]) };

        var body = await ResponseReader.ReadCappedAsync(response, CancellationToken.None);

        Assert.Equal(64 * 1024, body.Length);
    }

    [Fact]
    public async Task Read_ShortBody_IsWhole()
    {
        using var response = PipelineRig.Json(HttpStatusCode.OK, """{"id":"x"}""");

        Assert.Equal(10, (await ResponseReader.ReadCappedAsync(response, CancellationToken.None)).Length);
    }

    // Codes come from the provider's body; anything that is not a short token could echo an address or a message.
    [Theory]
    [InlineData("MS42207", "MS42207")]
    [InlineData("validation_error", "validation_error")]
    [InlineData("send-0008", "send-0008")]
    [InlineData("v1.2", "v1.2")]
    [InlineData("a@b.com", null)]
    [InlineData("has space", null)]
    [InlineData("MS42207\n", null)]
    [InlineData("line\nbreak", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ErrorCode_AllowList(string? code, string? expected)
        => Assert.Equal(expected, ResponseReader.SafeCode(code));

    [Fact]
    public void ErrorCode_AllowList_64KeptAnd65Dropped()
    {
        Assert.NotNull(ResponseReader.SafeCode(new string('a', 64)));
        Assert.Null(ResponseReader.SafeCode(new string('a', 65)));
    }

    [Fact]
    public void TryParse_Garbage_IsNull()
    {
        Assert.Null(ResponseReader.TryParse("<html>oops</html>"u8.ToArray()));
        Assert.Null(ResponseReader.TryParse(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void JsonHelpers_ReadOnlyTheTypeAsked()
    {
        using var doc = ResponseReader.TryParse("""{"s":"x","n":5,"o":{"s":"y"}}"""u8.ToArray())!;

        Assert.Equal("x", ResponseReader.String(doc.RootElement, "s"));
        Assert.Null(ResponseReader.String(doc.RootElement, "n"));
        Assert.Equal(5, ResponseReader.Int(doc.RootElement, "n"));
        Assert.Null(ResponseReader.Int(doc.RootElement, "s"));
        Assert.Equal("y", ResponseReader.String(doc.RootElement, "o", "s"));
        Assert.Null(ResponseReader.String(doc.RootElement, "missing", "s"));
    }
}
