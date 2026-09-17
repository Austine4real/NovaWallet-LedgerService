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

    public async Task<WalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct)
    {
        var exists = await _uow.Wallets.CustomerHasWalletAsync(request.CustomerId, ct);
        if (exists) throw new CustomerAlreadyHasWalletException(request.CustomerId);

        var wallet = Wallet.Create(request.CustomerId);
        _uow.Wallets.Add(wallet);
        await _uow.SaveChangesAsync(ct);

        return ToResponse(wallet);
    }

    public async Task<BalanceResponse> GetBalanceAsync(Guid walletId, CancellationToken ct)
    {
        var wallet = await _uow.Wallets.GetAsync(walletId, ct) ?? throw new WalletNotFoundException(walletId);
        return new BalanceResponse(wallet.Id, wallet.BalanceKobo, wallet.Currency);
    }

    /// <summary>
    /// Simulates an inbound NIP credit. Uses the same locked read as
    /// transfers so a credit can never race unsafely with a concurrent debit
    /// on the same wallet.
    /// </summary>
    public async Task<BalanceResponse> CreditWalletAsync(Guid walletId, CreditWalletRequest request, CancellationToken ct)
    {
        if (request.AmountKobo <= 0) throw new InvalidAmountException(request.AmountKobo);

        await using var tx = await _uow.BeginTransactionAsync(TransactionIsolation.ReadCommitted, ct);
        try
        {
            var wallet = await _uow.Wallets.GetForUpdateAsync(walletId, ct)
                ?? throw new WalletNotFoundException(walletId);

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

    public async Task<StatementResponse> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var walletExists = await _uow.Wallets.ExistsAsync(walletId, ct);
        if (!walletExists) throw new WalletNotFoundException(walletId);

        var total = await _uow.Transactions.CountAsync(walletId, ct);
        var pageItems = await _uow.Transactions.GetPageAsync(walletId, page, pageSize, ct);

        var items = pageItems
            .Select(t => new StatementEntry(t.Id, t.Type.ToString(), t.AmountKobo, t.TransferGroupId, t.CreatedAt))
            .ToList();

        return new StatementResponse(walletId, page, pageSize, total, items);
    }

    private static WalletResponse ToResponse(Wallet wallet)
        => new(wallet.Id, wallet.CustomerId, wallet.BalanceKobo, wallet.Currency, wallet.CreatedAt);
}
