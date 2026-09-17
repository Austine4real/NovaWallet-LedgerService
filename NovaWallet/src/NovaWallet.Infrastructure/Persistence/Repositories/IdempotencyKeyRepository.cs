using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Repositories;

public class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private readonly NovaWalletDbContext _db;

    public IdempotencyKeyRepository(NovaWalletDbContext db) => _db = db;

    public Task<IdempotencyKey?> FindAsync(string key, CancellationToken ct)
        => _db.IdempotencyKeys.SingleOrDefaultAsync(k => k.Key == key, ct);

    public void Add(IdempotencyKey entry) => _db.IdempotencyKeys.Add(entry);
}
