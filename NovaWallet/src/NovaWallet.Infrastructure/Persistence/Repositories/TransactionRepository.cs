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
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
