using System.Text.Json;
using System.Text.RegularExpressions;

namespace Umbraco.Community.EmailSender.Providers.Pipeline;

/// <summary>
/// Capped reads and safe JSON helpers. Only short tokens from a response ever reach error text or logs: provider messages can
/// echo addresses, subjects or keys (design 9.2, "Error text").
/// </summary>
internal static partial class ResponseReader
{
    /// <summary>Mandrill, Mailjet and Mailtrap return one entry per recipient, which outgrows 4 KB at 50 recipients.</summary>
    public const int MaxBytes = 64 * 1024;

    public static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), ct);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return buffer[..filled];
    }

    /// <summary>Null for an empty, truncated or non-JSON body. The caller disposes the document.</summary>
    public static JsonDocument? TryParse(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? String(JsonElement root, params string[] path)
        => Walk(root, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static int? Int(JsonElement root, params string[] path)
        => Walk(root, path) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;

    public static JsonElement? Walk(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>The code, or null unless it is a short token (the M3 Notify rule). <c>\z</c>, not <c>$</c>: <c>$</c> allows a trailing newline.</summary>
    public static string? SafeCode(string? code) => code is not null && CodePattern().IsMatch(code) ? code : null;

    /// <summary>A provider message id for the success log: printable ASCII without spaces, else null.</summary>
    public static string? SafeId(string? id) => id is not null && IdPattern().IsMatch(id) ? id : null;

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,64}\z")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[\x21-\x7E]{1,128}\z")]
    private static partial Regex IdPattern();
}
