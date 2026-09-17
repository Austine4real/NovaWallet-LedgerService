namespace NovaWallet.Application.DTOs;

public record CreateWalletRequest(string CustomerId);

public record WalletResponse(Guid WalletId, string CustomerId, long BalanceKobo, string Currency, DateTimeOffset CreatedAt);

public record BalanceResponse(Guid WalletId, long BalanceKobo, string Currency);

public record CreditWalletRequest(long AmountKobo, string? Narration);

public record StatementEntry(
    Guid TransactionId,
    string Type,
    long AmountKobo,
    Guid? TransferGroupId,
    DateTimeOffset CreatedAt);

public record StatementResponse(
    Guid WalletId,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<StatementEntry> Items);
