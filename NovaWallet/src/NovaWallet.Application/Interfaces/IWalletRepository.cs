using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

public interface IWalletRepository
{
    void Add(Wallet wallet);

    Task<bool> CustomerHasWalletAsync(string customerId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid walletId, CancellationToken ct);

    /// <summary>Looks up a customer's existing wallet by customer id (not wallet id) - used
    /// to report which wallet already exists when CreateWallet hits a duplicate.</summary>
    Task<Wallet?> GetByCustomerIdAsync(string customerId, CancellationToken ct);

    /// <summary>
    /// Acquires a transaction-scoped SQL Server application lock keyed on the
    /// customer id, before any row exists to place a row-lock on. This closes
    /// the check-then-insert race in CreateWallet at its root: two concurrent
    /// requests for the same customer id now serialize on this lock, so the
    /// second one only reaches the "does a wallet already exist?" check after
    /// the first has already committed (or rolled back) - it never gets a
    /// chance to see a false "not yet created" and insert a duplicate.
    /// </summary>
    Task AcquireCustomerCreationLockAsync(string customerId, CancellationToken ct);

    /// <summary>Plain read, no locking. Safe for balance inquiries / statements.</summary>
    Task<Wallet?> GetAsync(Guid walletId, CancellationToken ct);

    /// <summary>
    /// Reads a wallet with a database-level pessimistic lock, held for the
    /// lifetime of the enclosing transaction. Must only be called inside an
    /// active transaction opened via IUnitOfWork.BeginTransactionAsync. This
    /// is what makes concurrent balance mutations against the same wallet
    /// safe - see the SQL Server implementation for the exact mechanism.
    /// </summary>
    Task<Wallet?> GetForUpdateAsync(Guid walletId, CancellationToken ct);
}
