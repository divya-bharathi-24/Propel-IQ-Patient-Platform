using System.Text;

namespace Propel.Domain.Utilities;

/// <summary>
/// Encodes and decodes opaque keyset pagination cursors for the audit log endpoint (US_047, AC-1).
/// Format: Base64URL( "{timestamp_ticks}|{id}" ) — the pipe delimiter is safe inside a Base64 payload.
/// Cursor is intentionally opaque to callers — no semantic meaning is exposed in the API contract.
/// </summary>
public static class AuditCursorHelper
{
    private const char Delimiter = '|';

    /// <summary>
    /// Encodes a composite cursor from a UTC <paramref name="timestamp"/> and row <paramref name="id"/>.
    /// </summary>
    public static string Encode(DateTime timestamp, Guid id)
    {
        var raw = $"{timestamp.ToUniversalTime().Ticks}{Delimiter}{id}";
        return Base64UrlEncode(raw);
    }

    /// <summary>
    /// Decodes a cursor string produced by <see cref="Encode"/>.
    /// Returns null if the cursor is null, empty, or malformed (caller should treat as no cursor).
    /// </summary>
    public static (DateTime Timestamp, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var raw = Base64UrlDecode(cursor);
            var delimIndex = raw.IndexOf(Delimiter, StringComparison.Ordinal);
            if (delimIndex < 0)
                return null;

            var ticksPart = raw[..delimIndex];
            var idPart    = raw[(delimIndex + 1)..];

            if (!long.TryParse(ticksPart, out var ticks) || !Guid.TryParse(idPart, out var id))
                return null;

            var timestamp = new DateTime(ticks, DateTimeKind.Utc);
            return (timestamp, id);
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Base64UrlDecode(string input)
    {
        var base64 = input
            .Replace('-', '+')
            .Replace('_', '/');

        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }
}
