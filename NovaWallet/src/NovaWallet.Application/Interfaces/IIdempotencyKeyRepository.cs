using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

public interface IIdempotencyKeyRepository
{
    Task<IdempotencyKey?> FindAsync(string key, CancellationToken ct);
    void Add(IdempotencyKey entry);
}
