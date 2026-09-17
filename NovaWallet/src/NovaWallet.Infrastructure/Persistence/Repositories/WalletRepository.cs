using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Repositories;

public class WalletRepository : IWalletRepository
{
    private readonly NovaWalletDbContext _db;

    public WalletRepository(NovaWalletDbContext db) => _db = db;

    public void Add(Wallet wallet) => _db.Wallets.Add(wallet);

    public Task<bool> CustomerHasWalletAsync(string customerId, CancellationToken ct)
        => _db.Wallets.AnyAsync(w => w.CustomerId == customerId, ct);

    public Task<Wallet?> GetByCustomerIdAsync(string customerId, CancellationToken ct)
        => _db.Wallets.AsNoTracking().SingleOrDefaultAsync(w => w.CustomerId == customerId, ct);

    /// <summary>
    /// sp_getapplock is SQL Server's advisory-lock mechanism - a lock on a
    /// named resource string, not on any actual row. That's exactly what's
    /// needed here: before a customer has a wallet, there's no row to place
    /// UPDLOCK/ROWLOCK on (that's what GetForUpdateAsync uses elsewhere).
    /// @LockOwner = 'Transaction' ties the lock's lifetime to the enclosing
    /// transaction, so it releases automatically on commit or rollback - no
    /// manual unlock call needed. The customer id is hashed only to keep the
    /// resource name short and free of characters sp_getapplock might treat
    /// specially; it's not for secrecy.
    /// </summary>
    public async Task AcquireCustomerCreationLockAsync(string customerId, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(customerId)));
        var resource = $"NovaWallet:CreateWallet:{hash}";

        await _db.Database.ExecuteSqlInterpolatedAsync($$"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {{resource}},
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;

            IF @result < 0
                THROW 51003, 'Unable to acquire wallet creation lock.', 1;
            """, ct);
    }

    public Task<bool> ExistsAsync(Guid walletId, CancellationToken ct)
        => _db.Wallets.AsNoTracking().AnyAsync(w => w.Id == walletId, ct);

    public Task<Wallet?> GetAsync(Guid walletId, CancellationToken ct)
        => _db.Wallets.AsNoTracking().SingleOrDefaultAsync(w => w.Id == walletId, ct);

    /// <summary>
    /// Reads a single wallet row with a SQL Server pessimistic lock hint.
    /// UPDLOCK takes an update lock immediately on read (rather than a shared
    /// lock that gets upgraded later), and ROWLOCK asks the engine not to
    /// escalate to a page/table lock. Combined with an explicit transaction
    /// (opened via UnitOfWork.BeginTransactionAsync), this means: once one
    /// request has read a wallet for update, any other request trying to
    /// read the *same* wallet for update blocks until the first transaction
    /// commits or rolls back.
    /// </summary>
    public Task<Wallet?> GetForUpdateAsync(Guid walletId, CancellationToken ct)
    {
        return _db.Wallets
            .FromSqlInterpolated($"SELECT * FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE Id = {walletId}")
            .SingleOrDefaultAsync(ct);
    }
}
