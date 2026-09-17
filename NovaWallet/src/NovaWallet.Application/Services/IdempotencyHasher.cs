using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NovaWallet.Application.Services;


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
