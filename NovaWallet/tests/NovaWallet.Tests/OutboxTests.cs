using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using Xunit;

namespace NovaWallet.Tests;

public class OutboxTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public OutboxTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SuccessfulTransfer_WritesUnpublishedTransferCompletedEvent()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromWallet = await walletService.CreateWalletAsync(
            new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);
        await walletService.CreditWalletAsync(
            fromWallet.WalletId, new CreditWalletRequest(500_00, "opening balance"), CancellationToken.None);
        var toWallet = await walletService.CreateWalletAsync(
            new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);

        var result = await transferService.TransferAsync(
            new TransferRequest(fromWallet.WalletId, toWallet.WalletId, 100_00, "outbox test"),
            Guid.NewGuid().ToString(),
            CancellationToken.None);

        // GetUnpublishedAsync intentionally returns oldest-first, unbounded by
        // which transfer wrote them - a wide batch size here is just to make
        // sure this test's own event is definitely included.
        var pending = await uow.Outbox.GetUnpublishedAsync(batchSize: 100, CancellationToken.None);

        Assert.Contains(pending, m => m.Type == "TransferCompleted" && m.Payload.Contains(result.TransferGroupId.ToString()));
    }
}
