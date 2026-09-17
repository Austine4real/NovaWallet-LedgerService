using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Common;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Application.Services;

public class WalletService
{
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public WalletService(IUnitOfWork uow, IClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<WalletResponse> CreateWalletAsync(CreateWalletRequest request, string callerCustomerId, CancellationToken ct)
    {
        // A caller may only ever create a wallet for their own customer id -
        // otherwise any authenticated caller could provision wallets on
        // behalf of arbitrary customers.
        if (!string.Equals(request.CustomerId, callerCustomerId, StringComparison.Ordinal))
            throw new ForbiddenException("You can only create a wallet for your own customer id.");

        // Acquiring the customer-scoped lock BEFORE checking existence closes
        // the check-then-insert race at its root: two concurrent requests for
        // the same customer id now serialize here, so the second one only
        // reaches the existence check after the first has already committed
        // (or rolled back) - it can never see a false "not yet created".
        // (UnitOfWork.SaveChangesAsync still translates a raw unique-constraint
        // violation into the same exception as a defensive backstop, but with
        // this lock in place that path should never actually be exercised.)
        await using var tx = await _uow.BeginTransactionAsync(TransactionIsolation.ReadCommitted, ct);
        try
        {
            await _uow.Wallets.AcquireCustomerCreationLockAsync(request.CustomerId, ct);

            var existingWallet = await _uow.Wallets.GetByCustomerIdAsync(request.CustomerId, ct);
            if (existingWallet is not null)
                throw new CustomerAlreadyHasWalletException(request.CustomerId, existingWallet.Id);

            var wallet = Wallet.Create(request.CustomerId);
            _uow.Wallets.Add(wallet);
            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return ToResponse(wallet);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<BalanceResponse> GetBalanceAsync(Guid walletId, string callerCustomerId, CancellationToken ct)
    {
        var wallet = await _uow.Wallets.GetAsync(walletId, ct) ?? throw new WalletNotFoundException(walletId);
        EnsureOwnedBy(wallet, callerCustomerId);
        return new BalanceResponse(wallet.Id, wallet.BalanceKobo, wallet.Currency);
    }

    /// <summary>
    /// Simulates an inbound NIP credit. Uses the same locked read as
    /// transfers so a credit can never race unsafely with a concurrent debit
    /// on the same wallet.
    ///
    /// Ownership check note: a real inbound-settlement credit would most
    /// likely be triggered by a trusted internal service (NIBSS webhook),
    /// not the customer's own session - but this API has no such second
    /// actor, only the customer-facing JWT. Enforcing ownership here too is
    /// the safer default for what this endpoint can otherwise be used for
    /// today, and is documented as an explicit assumption in the README.
    /// </summary>
    public async Task<BalanceResponse> CreditWalletAsync(Guid walletId, CreditWalletRequest request, string callerCustomerId, CancellationToken ct)
    {
        if (request.AmountKobo <= 0) throw new InvalidAmountException(request.AmountKobo);

        await using var tx = await _uow.BeginTransactionAsync(TransactionIsolation.ReadCommitted, ct);
        try
        {
            var wallet = await _uow.Wallets.GetForUpdateAsync(walletId, ct)
                ?? throw new WalletNotFoundException(walletId);
            EnsureOwnedBy(wallet, callerCustomerId);

            wallet.Credit(request.AmountKobo);

            var today = _clock.UtcNow.ToWatDate();
            _uow.Transactions.Add(Transaction.Create(wallet.Id, TransactionType.CreditIn, request.AmountKobo, today));
            _uow.AuditLog.Add(AuditLogEntry.Create(
                wallet.Id, request.AmountKobo, wallet.BalanceKobo, request.Narration ?? "Inbound NIP credit"));

            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new BalanceResponse(wallet.Id, wallet.BalanceKobo, wallet.Currency);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<StatementResponse> GetStatementAsync(Guid walletId, int page, int pageSize, string callerCustomerId, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var wallet = await _uow.Wallets.GetAsync(walletId, ct) ?? throw new WalletNotFoundException(walletId);
        EnsureOwnedBy(wallet, callerCustomerId);

        var total = await _uow.Transactions.CountAsync(walletId, ct);
        var pageItems = await _uow.Transactions.GetPageAsync(walletId, page, pageSize, ct);

        var items = pageItems
            .Select(t => new StatementEntry(t.Id, t.Type.ToString(), t.AmountKobo, t.TransferGroupId, t.CreatedAt))
            .ToList();

        return new StatementResponse(walletId, page, pageSize, total, items);
    }

    private static void EnsureOwnedBy(Wallet wallet, string callerCustomerId)
    {
        if (!string.Equals(wallet.CustomerId, callerCustomerId, StringComparison.Ordinal))
            throw new ForbiddenException($"You do not have access to wallet '{wallet.Id}'.");
    }

    private static WalletResponse ToResponse(Wallet wallet)
        => new(wallet.Id, wallet.CustomerId, wallet.BalanceKobo, wallet.Currency, wallet.CreatedAt);
}
