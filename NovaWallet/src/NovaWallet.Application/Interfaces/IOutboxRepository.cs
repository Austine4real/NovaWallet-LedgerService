using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    /// <summary>Oldest-first batch of not-yet-published messages, for the publisher to process.</summary>
    Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int batchSize, CancellationToken ct);

    void MarkPublished(OutboxMessage message, DateTimeOffset publishedAt);
}
