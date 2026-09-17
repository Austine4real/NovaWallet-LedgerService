namespace NovaWallet.Application.DTOs;

/// <summary>The payload published via the outbox once a transfer commits.</summary>
public record TransferCompletedEvent(
    Guid TransferGroupId,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountKobo,
    DateTimeOffset CompletedAt);
