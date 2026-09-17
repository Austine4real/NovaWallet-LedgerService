using NovaWallet.Domain.Enums;

namespace NovaWallet.Domain.Entities;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public TransactionType Type { get; private set; }
    public long AmountKobo { get; private set; }
    public Guid? TransferGroupId { get; private set; }
    public DateOnly WatDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Transaction() { }

    public static Transaction Create(
        Guid walletId,
        TransactionType type,
        long amountKobo,
        DateOnly watDate,
        Guid? transferGroupId = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            Type = type,
            AmountKobo = amountKobo,
            TransferGroupId = transferGroupId,
            WatDate = watDate,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
