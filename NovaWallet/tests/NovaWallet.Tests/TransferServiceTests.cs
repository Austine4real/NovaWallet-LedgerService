using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

public class TransferServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TransferServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    private static async Task<(Guid WalletId, string CustomerId)> CreateFundedWalletAsync(WalletService walletService, long openingBalanceKobo)
    {
        var customerId = $"cust-{Guid.NewGuid():N}";
        var wallet = await walletService.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);
        if (openingBalanceKobo > 0)
            await walletService.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(openingBalanceKobo, "opening balance"), customerId, CancellationToken.None);
        return (wallet.WalletId, customerId);
    }

    [Fact]
    public async Task Transfer_MovesFundsBetweenWallets()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, 1_000_00);
        var (toId, toCustomerId) = await CreateFundedWalletAsync(walletService, 0);

        var result = await transferService.TransferAsync(
            new TransferRequest(fromId, toId, 400_00, "test transfer"), Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None);

        Assert.Equal(600_00, result.FromWalletBalanceAfterKobo);

        var toBalance = await walletService.GetBalanceAsync(toId, toCustomerId, CancellationToken.None);
        Assert.Equal(400_00, toBalance.BalanceKobo);
    }

    [Fact]
    public async Task Transfer_ByNonOwnerOfFromWallet_IsForbidden()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, _) = await CreateFundedWalletAsync(walletService, 1_000_00);
        var (toId, _) = await CreateFundedWalletAsync(walletService, 0);
        var someoneElseCustomerId = $"cust-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<ForbiddenException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 100_00, null), Guid.NewGuid().ToString(), someoneElseCustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Throws_AndDoesNotMutateBalances()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, 100_00);
        var (toId, toCustomerId) = await CreateFundedWalletAsync(walletService, 0);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 200_00, null), Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None));

        var fromBalance = await walletService.GetBalanceAsync(fromId, fromCustomerId, CancellationToken.None);
        var toBalance = await walletService.GetBalanceAsync(toId, toCustomerId, CancellationToken.None);
        Assert.Equal(100_00, fromBalance.BalanceKobo);
        Assert.Equal(0, toBalance.BalanceKobo);
    }

    [Fact]
    public async Task Transfer_ExceedingDailyLimit_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, TransferService.DailyLimitKobo + 1_00_00);
        var (toId, _) = await CreateFundedWalletAsync(walletService, 0);

        // First transfer consumes almost the entire daily limit.
        await transferService.TransferAsync(
            new TransferRequest(fromId, toId, TransferService.DailyLimitKobo, null), Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None);

        // A second, small transfer the same day should now breach the limit.
        await Assert.ThrowsAsync<DailyLimitExceededException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 1_00_00, null), Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_AtExactlyTheDailyLimit_Succeeds()
    {
        // Boundary case the earlier test suite didn't cover: exactly the
        // limit should succeed, not just "over the limit should fail."
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, TransferService.DailyLimitKobo);
        var (toId, _) = await CreateFundedWalletAsync(walletService, 0);

        var result = await transferService.TransferAsync(
            new TransferRequest(fromId, toId, TransferService.DailyLimitKobo, null), Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None);

        Assert.Equal(0, result.FromWalletBalanceAfterKobo);
    }

    [Fact]
    public async Task Transfer_SameKeyReplayed_IsProcessedOnce()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, 500_00);
        var (toId, _) = await CreateFundedWalletAsync(walletService, 0);
        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest(fromId, toId, 200_00, null);

        var first = await transferService.TransferAsync(request, idempotencyKey, fromCustomerId, CancellationToken.None);
        var replay = await transferService.TransferAsync(request, idempotencyKey, fromCustomerId, CancellationToken.None);

        Assert.Equal(first.TransferGroupId, replay.TransferGroupId);

        var fromBalance = await walletService.GetBalanceAsync(fromId, fromCustomerId, CancellationToken.None);
        Assert.Equal(300_00, fromBalance.BalanceKobo); // debited only once
    }

    [Fact]
    public async Task Transfer_SameKeyDifferentPayload_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(walletService, 500_00);
        var (toId, _) = await CreateFundedWalletAsync(walletService, 0);
        var idempotencyKey = Guid.NewGuid().ToString();

        await transferService.TransferAsync(
            new TransferRequest(fromId, toId, 100_00, null), idempotencyKey, fromCustomerId, CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 200_00, null), idempotencyKey, fromCustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_SameKeySamePayload_ConcurrentReplays_ProcessedExactlyOnce()
    {
        // The scenario the ordering fix (lock wallets before checking the
        // idempotency key) specifically exists for: fire the SAME request
        // with the SAME key truly concurrently, not sequentially. Every
        // response should agree on the same TransferGroupId, and the debit
        // should have happened exactly once.
        await using var setupUow = _fixture.CreateUnitOfWork();
        var setupWalletService = new WalletService(setupUow, new TestClock());
        var (fromId, fromCustomerId) = await CreateFundedWalletAsync(setupWalletService, 500_00);
        var (toId, _) = await CreateFundedWalletAsync(setupWalletService, 0);

        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest(fromId, toId, 200_00, "concurrent replay test");

        var results = new System.Collections.Concurrent.ConcurrentBag<TransferResponse>();
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var uow = _fixture.CreateUnitOfWork();
            var transferService = new TransferService(uow, new TestClock());
            var result = await transferService.TransferAsync(request, idempotencyKey, fromCustomerId, CancellationToken.None);
            results.Add(result);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(8, results.Count);
        Assert.Single(results.Select(r => r.TransferGroupId).Distinct());

        await using var verifyUow = _fixture.CreateUnitOfWork();
        var verifyWalletService = new WalletService(verifyUow, new TestClock());
        var fromBalance = await verifyWalletService.GetBalanceAsync(fromId, fromCustomerId, CancellationToken.None);
        Assert.Equal(300_00, fromBalance.BalanceKobo); // debited exactly once, not 8 times
    }
}
