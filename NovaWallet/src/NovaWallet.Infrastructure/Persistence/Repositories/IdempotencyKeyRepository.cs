using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Same sp_getapplock mechanism as WalletRepository's creation lock, but
    /// keyed on the idempotency key itself rather than a customer id -
    /// see the interface doc comment for why this closes the residual
    /// concurrency gap that wallet-locking alone couldn't cover.
    /// </summary>
    public async Task AcquireProcessingLockAsync(string idempotencyKey, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        var resource = $"NovaWallet:IdempotencyKey:{hash}";

        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;

            IF @result < 0
                THROW 51004, 'Unable to acquire idempotency-key processing lock.', 1;
            """, ct);
    }
}
