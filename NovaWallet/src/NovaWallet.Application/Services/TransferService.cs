using System.Text.Json;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Interfaces;
using NovaWallet.Domain.Common;
using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Enums;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Application.Services;

public class TransferService
{
    // NGN 500,000 expressed in kobo (100 kobo = NGN 1).
    public const long DailyLimitKobo = 500_000_00;

    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public TransferService(IUnitOfWork uow, IClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, string callerCustomerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new IdempotencyKeyMissingException();

        if (request.AmountKobo <= 0)
            throw new InvalidAmountException(request.AmountKobo);

        if (request.FromWalletId == request.ToWalletId)
            throw new InvalidTransferException("Cannot transfer a wallet to itself.");

        var requestHash = IdempotencyHasher.Hash(request);

        // ReadCommitted is sufficient here because every balance-affecting read
        // goes through GetForUpdateAsync, which takes an explicit database
        // row lock - that lock, not the isolation level, is what prevents a
        // concurrent transfer from reading a stale balance.
        await using var tx = await _uow.BeginTransactionAsync(TransactionIsolation.ReadCommitted, ct);
        try
        {
            // --- Lock both wallets FIRST, in a fixed, deterministic order
            // --- (independent of which one is "from" and which is "to") so
            // --- two transfers moving money in opposite directions between
            // --- the same pair of wallets can never deadlock each other.
            //
            // --- Locking before the idempotency check (rather than after) is
            // --- deliberate: a genuine replay of the same Idempotency-Key
            // --- carries the same payload, and therefore targets the same
            // --- two wallets. Two concurrent replays contend on the same
            // --- wallet locks, so the second one only reaches the
            // --- idempotency check after the first has already committed -
            // --- guaranteeing it sees "already processed" instead of racing
            // --- the first request to a raw primary-key conflict.
            var (firstId, secondId) = OrderIds(request.FromWalletId, request.ToWalletId);
            var firstWallet = await _uow.Wallets.GetForUpdateAsync(firstId, ct)
                ?? throw new WalletNotFoundException(firstId);
            var secondWallet = await _uow.Wallets.GetForUpdateAsync(secondId, ct)
                ?? throw new WalletNotFoundException(secondId);

            var fromWallet = firstWallet.Id == request.FromWalletId ? firstWallet : secondWallet;
            var toWallet = firstWallet.Id == request.ToWalletId ? firstWallet : secondWallet;

            // Ownership check happens here - after locking, before the
            // idempotency check - so it applies uniformly whether this turns
            // out to be a brand new transfer or a replay. Only the sender
            // needs to be the caller; the recipient wallet can belong to
            // anyone (that's the point of a P2P transfer).
            if (!string.Equals(fromWallet.CustomerId, callerCustomerId, StringComparison.Ordinal))
                throw new ForbiddenException($"You do not have access to wallet '{fromWallet.Id}'.");

            var existingKey = await _uow.IdempotencyKeys.FindAsync(idempotencyKey, ct);
            if (existingKey is not null)
            {
                if (existingKey.RequestHash != requestHash)
                    throw new IdempotencyKeyConflictException(idempotencyKey);

                await tx.CommitAsync(ct);
                return JsonSerializer.Deserialize<TransferResponse>(existingKey.ResponseBody)!;
            }

            // --- Daily outbound limit, reset at WAT midnight. Computed from
            // --- the already-committed TransferOut rows for today, read
            // --- under lock.
            var today = _clock.UtcNow.ToWatDate();
            var alreadySentToday = await _uow.Transactions.SumAmountAsync(
                fromWallet.Id, TransactionType.TransferOut, today, ct);

            if (alreadySentToday + request.AmountKobo > DailyLimitKobo)
                throw new DailyLimitExceededException(fromWallet.Id, DailyLimitKobo);

            fromWallet.Debit(request.AmountKobo);
            toWallet.Credit(request.AmountKobo);

            var transferGroupId = Guid.NewGuid();
            _uow.Transactions.Add(Transaction.Create(fromWallet.Id, TransactionType.TransferOut, request.AmountKobo, today, transferGroupId));
            _uow.Transactions.Add(Transaction.Create(toWallet.Id, TransactionType.TransferIn, request.AmountKobo, today, transferGroupId));

            _uow.AuditLog.Add(AuditLogEntry.Create(
                fromWallet.Id, -request.AmountKobo, fromWallet.BalanceKobo,
                request.Narration ?? "Transfer out", transferGroupId));
            _uow.AuditLog.Add(AuditLogEntry.Create(
                toWallet.Id, request.AmountKobo, toWallet.BalanceKobo,
                request.Narration ?? "Transfer in", transferGroupId));

            var response = new TransferResponse(
                transferGroupId, fromWallet.Id, toWallet.Id, request.AmountKobo,
                fromWallet.BalanceKobo, _clock.UtcNow);

            _uow.IdempotencyKeys.Add(IdempotencyKey.Create(idempotencyKey, requestHash, JsonSerializer.Serialize(response)));

            // --- Outbox: written in the SAME transaction, and therefore the
            // --- same commit, as the transfer itself. This is the entire
            // --- point of the pattern - the event cannot be recorded unless
            // --- the transfer actually committed, and cannot be silently
            // --- lost if it did (a background publisher, not shown here in
            // --- the request path, later delivers it - see
            // --- OutboxPublisherService).
            var eventPayload = JsonSerializer.Serialize(new TransferCompletedEvent(
                transferGroupId, fromWallet.Id, toWallet.Id, request.AmountKobo, response.CompletedAt));
            _uow.Outbox.Add(OutboxMessage.Create("TransferCompleted", eventPayload));

            await _uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return response;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // Consistent ordering (ordinal string comparison of the GUIDs) applied on
    // every call, regardless of transfer direction - this is what avoids
    // deadlocks between two wallets transferring to each other simultaneously.
    private static (Guid First, Guid Second) OrderIds(Guid a, Guid b)
        => string.CompareOrdinal(a.ToString(), b.ToString()) <= 0 ? (a, b) : (b, a);
}
