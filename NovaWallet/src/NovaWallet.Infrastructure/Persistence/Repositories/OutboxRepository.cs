using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly NovaWalletDbContext _db;

    public OutboxRepository(NovaWalletDbContext db) => _db = db;

    public void Add(OutboxMessage message) => _db.OutboxMessages.Add(message);

    public async Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int batchSize, CancellationToken ct)
    {
        return await _db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public void MarkPublished(OutboxMessage message, DateTimeOffset publishedAt) => message.MarkPublished(publishedAt);
}
