namespace NovaWallet.Application.Interfaces;

/// <summary>
/// Deliberately narrower than EF Core's System.Data.IsolationLevel - the
/// Application layer only needs to express two concepts, not the full ADO.NET
/// enum, keeping this interface free of any database-technology dependency.
/// </summary>
public enum TransactionIsolation
{
    ReadCommitted,
    Serializable
}

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

/// <summary>
/// Aggregates all repositories that participate in a single unit of work,
/// plus the transaction/save-changes coordination across them. Application
/// services depend on this single interface rather than on EF Core's DbSet
/// or DbContext types directly - Application has zero package dependency on
/// EF Core as a result (see NovaWallet.Application.csproj).
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IWalletRepository Wallets { get; }
    ITransactionRepository Transactions { get; }
    IAuditLogRepository AuditLog { get; }
    IIdempotencyKeyRepository IdempotencyKeys { get; }
    IOutboxRepository Outbox { get; }

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(TransactionIsolation level, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
