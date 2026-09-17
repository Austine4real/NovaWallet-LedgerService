using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

public class WalletServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WalletServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateWallet_StartsAtZeroBalance()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());

        var customerId = $"cust-{Guid.NewGuid():N}";
        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);

        Assert.Equal(0, wallet.BalanceKobo);
        Assert.Equal("NGN", wallet.Currency);
    }

    [Fact]
    public async Task CreateWallet_ForSomeoneElse_IsForbidden()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());

        var targetCustomerId = $"cust-{Guid.NewGuid():N}";
        var callerCustomerId = $"cust-{Guid.NewGuid():N}"; // a different customer entirely

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CreateWalletAsync(new CreateWalletRequest(targetCustomerId), callerCustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWallet_DuplicateCustomer_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var customerId = $"cust-{Guid.NewGuid():N}";

        await service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);

        await Assert.ThrowsAsync<CustomerAlreadyHasWalletException>(
            () => service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateWallet_ConcurrentDuplicateCustomer_OneWinsOneGetsCleanConflict()
    {
        // Two requests for the SAME customer id, truly concurrent, each with
        // its own UnitOfWork/DbContext - mirrors two separate HTTP requests
        // racing each other. Proves the check-then-insert race is backstopped
        // by the database's unique index and surfaces as the same clean
        // CustomerAlreadyHasWalletException a sequential duplicate gets,
        // rather than a raw, unhandled database error.
        var customerId = $"cust-{Guid.NewGuid():N}";
        var exceptions = new List<Exception>();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var uow = _fixture.CreateUnitOfWork();
            var service = new WalletService(uow, new TestClock());
            try
            {
                await service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);
                Interlocked.Increment(ref successCount);
            }
            catch (CustomerAlreadyHasWalletException ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        Assert.Equal(1, successCount);
        Assert.Equal(4, exceptions.Count);
        Assert.All(exceptions, ex => Assert.IsType<CustomerAlreadyHasWalletException>(ex));
    }

    [Fact]
    public async Task Credit_IncreasesBalance()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var customerId = $"cust-{Guid.NewGuid():N}";

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);
        var balance = await service.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(500_00, "test credit"), customerId, CancellationToken.None);

        Assert.Equal(500_00, balance.BalanceKobo);
    }

    [Fact]
    public async Task Credit_NonPositiveAmount_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var customerId = $"cust-{Guid.NewGuid():N}";

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidAmountException>(
            () => service.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(0, null), customerId, CancellationToken.None));
    }

    [Fact]
    public async Task Credit_ByNonOwner_IsForbidden()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var ownerCustomerId = $"cust-{Guid.NewGuid():N}";
        var otherCustomerId = $"cust-{Guid.NewGuid():N}";

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(ownerCustomerId), ownerCustomerId, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(100_00, null), otherCustomerId, CancellationToken.None));
    }

    [Fact]
    public async Task GetBalance_ByNonOwner_IsForbidden()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var ownerCustomerId = $"cust-{Guid.NewGuid():N}";
        var otherCustomerId = $"cust-{Guid.NewGuid():N}";

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(ownerCustomerId), ownerCustomerId, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetBalanceAsync(wallet.WalletId, otherCustomerId, CancellationToken.None));
    }
}
