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
