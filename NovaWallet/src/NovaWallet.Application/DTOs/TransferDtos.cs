namespace NovaWallet.Application.DTOs;

public record TransferRequest(Guid FromWalletId, Guid ToWalletId, long AmountKobo, string? Narration);

public record TransferResponse(
    Guid TransferGroupId,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountKobo,
    long FromWalletBalanceAfterKobo,
    DateTimeOffset CompletedAt);
