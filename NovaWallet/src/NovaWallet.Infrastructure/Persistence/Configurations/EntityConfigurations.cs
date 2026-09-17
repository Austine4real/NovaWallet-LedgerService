using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        // These CHECK constraints mirror the ones added by the
        // HardenLedgerInvariants migration - declaring them here too keeps
        // EF Core's own model in sync with what's actually in the database,
        // which is required for `dotnet ef database update` to not complain
        // about "pending model changes" against the migration snapshot.
        builder.ToTable("Wallets", table =>
        {
            table.HasCheckConstraint("CK_Wallets_BalanceKobo_NonNegative", "[BalanceKobo] >= 0");
            table.HasCheckConstraint("CK_Wallets_Currency_NGN", "[Currency] = 'NGN'");
        });
        builder.HasKey(w => w.Id);
        builder.Property(w => w.CustomerId).HasMaxLength(64).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.BalanceKobo).IsRequired();
        builder.HasIndex(w => w.CustomerId).IsUnique();
    }
}

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_AmountKobo_Positive", "[AmountKobo] > 0");
        });
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AmountKobo).IsRequired();
        builder.Property(t => t.Type).HasConversion<int>().IsRequired();
        // Foreign key added by the HardenLedgerInvariants migration - a
        // transaction can never reference a wallet that doesn't exist.
        // Restrict (not Cascade) because a wallet should never be deletable
        // while it still has transaction history.
        builder.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
        // Composite index supports both "statement" pagination and the
        // daily-limit sum query efficiently.
        builder.HasIndex(t => new { t.WalletId, t.CreatedAt });
        builder.HasIndex(t => new { t.WalletId, t.Type, t.WatDate });
    }
}

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries", table =>
        {
            // SQL Server disallows the OUTPUT clause EF Core normally uses
            // to read back generated values (the identity Id here) on tables
            // that have certain triggers enabled. This keeps EF's INSERT
            // statements compatible with the immutable-audit UPDATE/DELETE
            // trigger the HardenLedgerInvariants migration creates on this
            // table - without it, inserting an AuditLogEntry throws at
            // runtime even though nothing here ever updates or deletes one.
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_AuditLog_ResultingBalance_NonNegative", "[ResultingBalanceKobo] >= 0");
            table.HasCheckConstraint("CK_AuditLog_Delta_NonZero", "[DeltaKobo] <> 0");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.Description).HasMaxLength(256).IsRequired();
        builder.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(a => a.WalletId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.WalletId);
    }
}

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");
        builder.HasKey(k => k.Key);
        builder.Property(k => k.Key).HasMaxLength(128);
        builder.Property(k => k.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(k => k.ResponseBody).IsRequired();
    }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        // Publisher polls exactly this shape: oldest unpublished rows first.
        builder.HasIndex(m => new { m.PublishedAt, m.CreatedAt });
    }
}
