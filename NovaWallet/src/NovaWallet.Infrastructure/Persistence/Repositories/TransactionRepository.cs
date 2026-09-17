using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;

namespace NovaWallet.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly NovaWalletDbContext _db;

    public TransactionRepository(NovaWalletDbContext db) => _db = db;

    public void Add(Transaction transaction) => _db.Transactions.Add(transaction);

    public async Task<long> SumAmountAsync(Guid walletId, TransactionType type, DateOnly watDate, CancellationToken ct)
    {
        return await _db.Transactions
            .Where(t => t.WalletId == walletId && t.Type == type && t.WatDate == watDate)
            .SumAsync(t => (long?)t.AmountKobo, ct) ?? 0;
    }

    public Task<int> CountAsync(Guid walletId, CancellationToken ct)
        => _db.Transactions.AsNoTracking().Where(t => t.WalletId == walletId).CountAsync(ct);

    public async Task<IReadOnlyList<Transaction>> GetPageAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId)
            // CreatedAt alone isn't a safe sort key for pagination - two
            // transactions can share the same timestamp (DateTimeOffset
            // precision, or just two writes landing in the same tick), and
            // without a tiebreaker, SQL Server is free to order ties
            // differently between separate page queries, which can cause a
            // row to appear on two pages or on neither as data changes
            // underneath. Id is arbitrary but stable, which is all a
            // tiebreaker needs to be.
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
