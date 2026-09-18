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
            // --- Lock the idempotency key itself FIRST, before anything else.
            // ---
            // --- Locking the two wallets (further down) alone would only
            // --- serialize replays that happen to target the same wallets -
            // --- true for a genuine retry, but not for the narrower case of
            // --- the same key reused with a different payload pointing at
            // --- different wallets. An advisory lock keyed on the
            // --- idempotency key itself closes that gap too: ANY two
            // --- concurrent requests carrying the same key now serialize
            // --- here, regardless of which wallets their payloads reference.
            await _uow.IdempotencyKeys.AcquireProcessingLockAsync(idempotencyKey, ct);

            // --- Authorize against the source wallet before doing anything
            // --- else - including before returning a cached idempotent
            // --- response. Without this, knowing a valid Idempotency-Key and
            // --- payload could become a way to read the result of someone
            // --- else's transfer. A plain (unlocked) read is enough here:
            // --- CustomerId is immutable after a wallet is created, so this
            // --- check doesn't need the row lock that the actual balance
            // --- mutation further down does.
            var sourceForAuthorization = await _uow.Wallets.GetAsync(request.FromWalletId, ct)
                ?? throw new WalletNotFoundException(request.FromWalletId);
            if (!string.Equals(sourceForAuthorization.CustomerId, callerCustomerId, StringComparison.Ordinal))
                throw new ForbiddenException($"You do not have access to wallet '{sourceForAuthorization.Id}'.");


            var existingKey = await _uow.IdempotencyKeys.FindAsync(idempotencyKey, ct);
            if (existingKey is not null)
            {
                if (existingKey.RequestHash != requestHash)
                    throw new IdempotencyKeyConflictException(idempotencyKey);

                await tx.CommitAsync(ct);
                return JsonSerializer.Deserialize<TransferResponse>(existingKey.ResponseBody)!;
            }

            // --- New transfer: lock both wallets in a fixed, deterministic
            // --- order (independent of which one is "from" and which is
            // --- "to") so two transfers moving money in opposite directions
            // --- between the same pair of wallets can never deadlock.
            var (firstId, secondId) = OrderIds(request.FromWalletId, request.ToWalletId);
            var firstWallet = await _uow.Wallets.GetForUpdateAsync(firstId, ct)
                ?? throw new WalletNotFoundException(firstId);
            var secondWallet = await _uow.Wallets.GetForUpdateAsync(secondId, ct)
                ?? throw new WalletNotFoundException(secondId);

            var fromWallet = firstWallet.Id == request.FromWalletId ? firstWallet : secondWallet;
            var toWallet = firstWallet.Id == request.ToWalletId ? firstWallet : secondWallet;

            // --- Daily outbound limit, reset at WAT midnight. Computed from
            // --- the already-committed TransferOut rows for today, read
            // --- under lock. Written as a subtraction rather than
            // --- "alreadySent + requested > limit" so the comparison itself
            // --- can't overflow even in a pathological input case.
            var today = _clock.UtcNow.ToWatDate();
            var alreadySentToday = await _uow.Transactions.SumAmountAsync(
                fromWallet.Id, TransactionType.TransferOut, today, ct);

            if (alreadySentToday > DailyLimitKobo || request.AmountKobo > DailyLimitKobo - alreadySentToday)
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
