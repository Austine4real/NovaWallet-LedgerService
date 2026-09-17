using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

public interface IIdempotencyKeyRepository
{
    Task<IdempotencyKey?> FindAsync(string key, CancellationToken ct);
    void Add(IdempotencyKey entry);

    /// <summary>
    /// Acquires a transaction-scoped SQL Server application lock keyed on the
    /// idempotency key itself, before checking whether it's been used. This
    /// closes the one residual concurrency gap the wallet-lock ordering alone
    /// didn't cover: two concurrent requests reusing the same key but
    /// targeting *different* wallets (a client bug, but still shouldn't be
    /// able to cause a raw database error) now serialize here regardless of
    /// which wallets their payloads reference, since the lock is keyed on the
    /// idempotency key itself rather than on any wallet.
    /// </summary>
    Task AcquireProcessingLockAsync(string idempotencyKey, CancellationToken ct);
}
