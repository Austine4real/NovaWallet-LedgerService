using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NovaWallet.Application.Interfaces;
using NovaWallet.Infrastructure.Persistence.Repositories;

namespace NovaWallet.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly NovaWalletDbContext _db;

    public UnitOfWork(NovaWalletDbContext db)
    {
        _db = db;
        Wallets = new WalletRepository(db);
        Transactions = new TransactionRepository(db);
        AuditLog = new AuditLogRepository(db);
        IdempotencyKeys = new IdempotencyKeyRepository(db);
        Outbox = new OutboxRepository(db);
    }

    public IWalletRepository Wallets { get; }
    public ITransactionRepository Transactions { get; }
    public IAuditLogRepository AuditLog { get; }
    public IIdempotencyKeyRepository IdempotencyKeys { get; }
    public IOutboxRepository Outbox { get; }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(TransactionIsolation level, CancellationToken ct)
    {
        var isolationLevel = level switch
        {
            TransactionIsolation.Serializable => IsolationLevel.Serializable,
            _ => IsolationLevel.ReadCommitted
        };

        var efTransaction = await _db.Database.BeginTransactionAsync(isolationLevel, ct);
        return new EfUnitOfWorkTransaction(efTransaction);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private sealed class EfUnitOfWorkTransaction : IUnitOfWorkTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfUnitOfWorkTransaction(IDbContextTransaction transaction) => _transaction = transaction;

        public Task CommitAsync(CancellationToken ct) => _transaction.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct) => _transaction.RollbackAsync(ct);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
