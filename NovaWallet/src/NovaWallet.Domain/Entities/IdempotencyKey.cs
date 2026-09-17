namespace NovaWallet.Domain.Entities;

public class IdempotencyKey
{
    public string Key { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public string ResponseBody { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private IdempotencyKey() { }

    public static IdempotencyKey Create(string key, string requestHash, string responseBody)
    {
        return new IdempotencyKey
        {
            Key = key,
            RequestHash = requestHash,
            ResponseBody = responseBody,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
