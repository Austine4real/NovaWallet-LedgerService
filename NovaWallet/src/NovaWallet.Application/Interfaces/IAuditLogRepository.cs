using NovaWallet.Domain.Entities;

namespace NovaWallet.Application.Interfaces;

/// <summary>
/// Deliberately write-only (Add is the only member). There is no Update or
/// Delete anywhere on this interface - the append-only guarantee for the
/// audit trail is enforced by the shape of this abstraction itself, not
/// just by convention in the service layer.
/// </summary>
public interface IAuditLogRepository
{
    void Add(AuditLogEntry entry);
}
