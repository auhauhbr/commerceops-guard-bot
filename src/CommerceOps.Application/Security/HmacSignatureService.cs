using System.Security.Cryptography;
using System.Text;

namespace CommerceOps.Application.Security;

public sealed class HmacSignatureService
{
    public string ComputeSignature(string secret, string timestamp, string rawBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(timestamp);
        ArgumentNullException.ThrowIfNull(rawBody);

        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        var key = Encoding.UTF8.GetBytes(secret);

        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    public bool IsValidSignature(string secret, string timestamp, string rawBody, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(providedSignature))
        {
            return false;
        }

        var expectedSignature = ComputeSignature(secret, timestamp, rawBody);
        var normalizedProvidedSignature = NormalizeSignature(providedSignature);

        if (normalizedProvidedSignature.Length != expectedSignature.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedSignature),
            Encoding.ASCII.GetBytes(normalizedProvidedSignature));
    }

    private static string NormalizeSignature(string signature)
    {
        var trimmed = signature.Trim();
        const string prefix = "sha256=";

        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }
}
