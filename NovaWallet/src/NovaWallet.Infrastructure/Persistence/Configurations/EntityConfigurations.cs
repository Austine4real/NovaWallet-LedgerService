using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
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
        builder.ToTable("Transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AmountKobo).IsRequired();
        builder.Property(t => t.Type).HasConversion<int>().IsRequired();
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
        builder.ToTable("AuditLogEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.Description).HasMaxLength(256).IsRequired();
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
