namespace NovaWallet.Domain.Entities;

/// <summary>
/// Append-only record of every balance mutation. No application code path
/// updates or deletes rows in this table - it exists purely as an
/// immutable audit trail, separate from the queryable Transactions table.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; private set; }
    public Guid WalletId { get; private set; }
    public long DeltaKobo { get; private set; }
    public long ResultingBalanceKobo { get; private set; }
    public string Description { get; private set; } = default!;
    public Guid? TransferGroupId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(
        Guid walletId,
        long deltaKobo,
        long resultingBalanceKobo,
        string description,
        Guid? transferGroupId = null)
    {
        return new AuditLogEntry
        {
            WalletId = walletId,
            DeltaKobo = deltaKobo,
            ResultingBalanceKobo = resultingBalanceKobo,
            Description = description,
            TransferGroupId = transferGroupId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
