namespace NovaWallet.Domain.Entities;

/// <summary>
/// An event waiting to be published to the outside world (a message broker,
/// a webhook, etc.). Written inside the SAME database transaction as the
/// business operation that raised it (see TransferService), so the event
/// can never be recorded without the operation having actually committed,
/// and can never be silently lost if it did. A separate background process
/// (OutboxPublisherService) polls for unpublished rows and publishes them.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string type, string payload)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt;
}
