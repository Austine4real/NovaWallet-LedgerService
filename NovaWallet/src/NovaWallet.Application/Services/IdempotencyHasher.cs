using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NovaWallet.Application.Services;

/// <summary>
/// Produces a stable hash of a request payload so a replayed Idempotency-Key
/// can be checked against the *original* payload, not just trusted blindly.
/// </summary>
public static class IdempotencyHasher
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = null };

    public static string Hash<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
