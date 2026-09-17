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
        var wallet = await service.CreateWalletAsync(new CreateWalletRequest(customerId), CancellationToken.None);

        Assert.Equal(0, wallet.BalanceKobo);
        Assert.Equal("NGN", wallet.Currency);
    }

    [Fact]
    public async Task CreateWallet_DuplicateCustomer_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());
        var customerId = $"cust-{Guid.NewGuid():N}";

        await service.CreateWalletAsync(new CreateWalletRequest(customerId), CancellationToken.None);

        await Assert.ThrowsAsync<CustomerAlreadyHasWalletException>(
            () => service.CreateWalletAsync(new CreateWalletRequest(customerId), CancellationToken.None));
    }

    [Fact]
    public async Task Credit_IncreasesBalance()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);
        var balance = await service.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(500_00, "test credit"), CancellationToken.None);

        Assert.Equal(500_00, balance.BalanceKobo);
    }

    [Fact]
    public async Task Credit_NonPositiveAmount_Throws()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var service = new WalletService(uow, new TestClock());

        var wallet = await service.CreateWalletAsync(new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidAmountException>(
            () => service.CreditWalletAsync(wallet.WalletId, new CreditWalletRequest(0, null), CancellationToken.None));
    }
}
