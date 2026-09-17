using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure.Persistence.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly NovaWalletDbContext _db;

    public AuditLogRepository(NovaWalletDbContext db) => _db = db;

    public void Add(AuditLogEntry entry) => _db.AuditLogEntries.Add(entry);
}
