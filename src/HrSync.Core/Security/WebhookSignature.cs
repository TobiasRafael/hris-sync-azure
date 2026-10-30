using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HrSync.Core.Security;

// Signature = "sha256=" + lowercase hex HMAC-SHA256(secret, "{timestamp}.{body}").
// The timestamp is unix seconds and is signed too, so a captured request can't be replayed later.
public static class WebhookSignature
{
    public const string Prefix = "sha256=";
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    private const int HashSize = 32;

    public static string Compute(string secret, long timestamp, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(body);

        return Prefix + Convert.ToHexStringLower(Hash(secret, timestamp, body));
    }

    public static bool Verify(string secret, string? timestampHeader, string? signatureHeader, string body, DateTimeOffset now) =>
        Verify(secret, timestampHeader, signatureHeader, body, now, DefaultTolerance);

    public static bool Verify(
        string secret, string? timestampHeader, string? signatureHeader, string body, DateTimeOffset now, TimeSpan tolerance)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(body);

        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            return false;

        var nowSeconds = now.ToUnixTimeSeconds();
        var toleranceSeconds = (long)tolerance.TotalSeconds;
        if (timestamp < nowSeconds - toleranceSeconds || timestamp > nowSeconds + toleranceSeconds)
            return false;

        if (signatureHeader is null || !signatureHeader.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var hex = signatureHeader.AsSpan(Prefix.Length);
        if (hex.Length != HashSize * 2)
            return false;

        Span<byte> provided = stackalloc byte[HashSize];
        if (Convert.FromHexString(hex, provided, out _, out var written) != OperationStatus.Done || written != HashSize)
            return false;

        return CryptographicOperations.FixedTimeEquals(Hash(secret, timestamp, body), provided);
    }

    private static byte[] Hash(string secret, long timestamp, string body) =>
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}")));
}
