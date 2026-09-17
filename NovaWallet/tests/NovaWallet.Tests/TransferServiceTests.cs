using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

public class TransferServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TransferServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    private static async Task<Guid> CreateFundedWalletAsync(WalletService walletService, long openingBalanceKobo)
    {
        var wallet = await walletService.CreateWalletAsync(new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);
        if (openingBalanceKobo > 0)
            await walletService.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(openingBalanceKobo, "opening balance"), CancellationToken.None);
        return wallet.WalletId;
    }

    [Fact]
    public async Task Transfer_MovesFundsBetweenWallets()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromId = await CreateFundedWalletAsync(walletService, 1_000_00);
        var toId = await CreateFundedWalletAsync(walletService, 0);

        var result = await transferService.TransferAsync(
            new TransferRequest(fromId, toId, 400_00, "test transfer"), Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.Equal(600_00, result.FromWalletBalanceAfterKobo);

        var toBalance = await walletService.GetBalanceAsync(toId, CancellationToken.None);
        Assert.Equal(400_00, toBalance.BalanceKobo);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Throws_AndDoesNotMutateBalances()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromId = await CreateFundedWalletAsync(walletService, 100_00);
        var toId = await CreateFundedWalletAsync(walletService, 0);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 200_00, null), Guid.NewGuid().ToString(), CancellationToken.None));

        var fromBalance = await walletService.GetBalanceAsync(fromId, CancellationToken.None);
        var toBalance = await walletService.GetBalanceAsync(toId, CancellationToken.None);
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

        var fromId = await CreateFundedWalletAsync(walletService, TransferService.DailyLimitKobo + 1_00_00);
        var toId = await CreateFundedWalletAsync(walletService, 0);

        // First transfer consumes almost the entire daily limit.
        await transferService.TransferAsync(
            new TransferRequest(fromId, toId, TransferService.DailyLimitKobo, null), Guid.NewGuid().ToString(), CancellationToken.None);

        // A second, small transfer the same day should now breach the limit.
        await Assert.ThrowsAsync<DailyLimitExceededException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 1_00_00, null), Guid.NewGuid().ToString(), CancellationToken.None));
    }

    [Fact]
    public async Task Transfer_SameKeyReplayed_IsProcessedOnce()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromId = await CreateFundedWalletAsync(walletService, 500_00);
        var toId = await CreateFundedWalletAsync(walletService, 0);
        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest(fromId, toId, 200_00, null);

        var first = await transferService.TransferAsync(request, idempotencyKey, CancellationToken.None);
        var replay = await transferService.TransferAsync(request, idempotencyKey, CancellationToken.None);

        Assert.Equal(first.TransferGroupId, replay.TransferGroupId);

        var fromBalance = await walletService.GetBalanceAsync(fromId, CancellationToken.None);
        Assert.Equal(300_00, fromBalance.BalanceKobo); // debited only once
    }

    [Fact]
    public async Task Transfer_SameKeyDifferentPayload_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromId = await CreateFundedWalletAsync(walletService, 500_00);
        var toId = await CreateFundedWalletAsync(walletService, 0);
        var idempotencyKey = Guid.NewGuid().ToString();

        await transferService.TransferAsync(
            new TransferRequest(fromId, toId, 100_00, null), idempotencyKey, CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(() => transferService.TransferAsync(
            new TransferRequest(fromId, toId, 200_00, null), idempotencyKey, CancellationToken.None));
    }
}
