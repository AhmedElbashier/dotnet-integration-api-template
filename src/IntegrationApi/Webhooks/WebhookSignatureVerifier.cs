using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationApi.Webhooks;

public enum SignatureResult { Valid, MissingHeaders, BadTimestamp, Expired, Invalid }

/// <summary>
/// Verifies "X-Webhook-Signature: sha256=&lt;hex&gt;" where the HMAC is computed over
/// "{X-Webhook-Timestamp}.{raw body}". Same scheme family used by Stripe/Slack/etc.
/// Uses a constant-time comparison to avoid timing attacks.
/// </summary>
public sealed class WebhookSignatureVerifier(string secret, TimeSpan tolerance, TimeProvider clock)
{
    public const string SignatureHeader = "X-Webhook-Signature";
    public const string TimestampHeader = "X-Webhook-Timestamp";

    public static string ComputeSignature(string secret, string timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0);
        body.CopyTo(payload.AsSpan(prefix.Length));
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public SignatureResult Verify(string? signature, string? timestamp, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
            return SignatureResult.MissingHeaders;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
            return SignatureResult.BadTimestamp;

        var sent = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((clock.GetUtcNow() - sent).Duration() > tolerance)
            return SignatureResult.Expired;

        var expected = Encoding.UTF8.GetBytes(ComputeSignature(secret, timestamp, body));
        var actual = Encoding.UTF8.GetBytes(signature.Trim());
        return CryptographicOperations.FixedTimeEquals(expected, actual)
            ? SignatureResult.Valid
            : SignatureResult.Invalid;
    }
}
