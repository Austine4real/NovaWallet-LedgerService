using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Exceptions;
using NovaWallet.Infrastructure.Persistence.Repositories;

namespace NovaWallet.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    // SQL Server error numbers for a unique index/constraint violation.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

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

    /// <summary>
    /// Two concurrent CreateWallet calls for the same customer id can both
    /// pass the "does a wallet already exist?" check before either commits
    /// (that check and the insert aren't atomic together) - the database's
    /// unique index on Wallets.CustomerId is the real backstop. Without this
    /// translation, the second caller would see a raw, unhandled
    /// DbUpdateException surface as a generic 500 instead of the same clean
    /// 409 Conflict a sequential duplicate attempt gets. Wallets.CustomerId
    /// is the only non-primary-key unique constraint in the schema, so
    /// matching on "was a Wallet entity involved" is safe here without
    /// needing to parse the constraint name out of the error message.
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            var walletEntry = ex.Entries.Select(e => e.Entity).OfType<Wallet>().FirstOrDefault();
            if (walletEntry is not null)
            {
                // walletEntry.Id here is the rejected attempt's own id (assigned
                // client-side in Wallet.Create, never actually persisted) - not
                // the real existing wallet's id. Re-query by customer id to find
                // the wallet that's actually sitting in the database.
                var existing = await Wallets.GetByCustomerIdAsync(walletEntry.CustomerId, ct);
                throw new CustomerAlreadyHasWalletException(walletEntry.CustomerId, existing?.Id ?? Guid.Empty);
            }

            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sqlEx
           && (sqlEx.Number == UniqueConstraintViolation || sqlEx.Number == UniqueIndexViolation);

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
