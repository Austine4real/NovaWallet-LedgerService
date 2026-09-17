using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;

namespace NovaWallet.Application.Interfaces;

public interface ITransactionRepository
{
    void Add(Transaction transaction);

    /// <summary>Sum of amounts for a wallet/type/day - used for the daily outbound limit check.</summary>
    Task<long> SumAmountAsync(Guid walletId, TransactionType type, DateOnly watDate, CancellationToken ct);

    Task<int> CountAsync(Guid walletId, CancellationToken ct);

    Task<IReadOnlyList<Transaction>> GetPageAsync(Guid walletId, int page, int pageSize, CancellationToken ct);
}
