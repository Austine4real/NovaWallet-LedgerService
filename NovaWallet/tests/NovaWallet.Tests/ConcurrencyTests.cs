using System.Collections.Concurrent;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// The hard constraint under test: "Balance must never go negative under any
/// interleaving of concurrent requests." Each simulated concurrent request
/// gets its own UnitOfWork (and therefore its own DbContext), mirroring the
/// real request-scoped lifetime used by the running API (see Program.cs's
/// AddScoped&lt;IUnitOfWork, UnitOfWork&gt; registration) - so we're genuinely
/// exercising the database's row-locking behaviour, not just in-process C#
/// locking.
/// </summary>
public class ConcurrencyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private Guid _sourceWalletId;
    private Guid _sinkWalletId;

    public ConcurrencyTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ConcurrentTransfers_FromSameWallet_NeverGoNegative()
    {
        // Arrange: a wallet funded with exactly 10 units of 1,000 kobo.
        // Fire 30 concurrent transfer-out requests of 1,000 kobo each -
        // only 10 can possibly succeed.
        const long openingBalance = 10_000; // kobo
        const long transferAmount = 1_000;  // kobo
        const int concurrentRequests = 30;

        await using (var setupUow = _fixture.CreateUnitOfWork())
        {
            var walletService = new WalletService(setupUow, new TestClock());
            _sourceWalletId = await CreateFundedWalletAsync(walletService, openingBalance);
            _sinkWalletId = (await walletService.CreateWalletAsync(
                new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None)).WalletId;
        }

        var successCount = 0;
        var insufficientFundsCount = 0;
        var unexpectedExceptions = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            // Each concurrent "request" gets its own UnitOfWork/DbContext and
            // its own idempotency key, exactly as separate concurrent HTTP
            // requests would in the running API.
            await using var uow = _fixture.CreateUnitOfWork();
            var transferService = new TransferService(uow, new TestClock());

            try
            {
                await transferService.TransferAsync(
                    new TransferRequest(_sourceWalletId, _sinkWalletId, transferAmount, "concurrency test"),
                    Guid.NewGuid().ToString(),
                    CancellationToken.None);
                Interlocked.Increment(ref successCount);
            }
            catch (InsufficientFundsException)
            {
                Interlocked.Increment(ref insufficientFundsCount);
            }
            catch (Exception ex)
            {
                unexpectedExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: only exactly as many transfers as the opening balance allows
        // succeeded - no double-spend, no lost updates.
        Assert.Empty(unexpectedExceptions);
        Assert.Equal(openingBalance / transferAmount, successCount);
        Assert.Equal(concurrentRequests - successCount, insufficientFundsCount);

        await using var verifyUow = _fixture.CreateUnitOfWork();
        var verifyWalletService = new WalletService(verifyUow, new TestClock());

        var sourceBalance = await verifyWalletService.GetBalanceAsync(_sourceWalletId, CancellationToken.None);
        var sinkBalance = await verifyWalletService.GetBalanceAsync(_sinkWalletId, CancellationToken.None);

        Assert.Equal(0, sourceBalance.BalanceKobo);
        Assert.True(sourceBalance.BalanceKobo >= 0, "Balance must never go negative.");
        Assert.Equal(successCount * transferAmount, sinkBalance.BalanceKobo);
    }

    private static async Task<Guid> CreateFundedWalletAsync(WalletService walletService, long openingBalanceKobo)
    {
        var wallet = await walletService.CreateWalletAsync(
            new CreateWalletRequest($"cust-{Guid.NewGuid():N}"), CancellationToken.None);
        await walletService.CreditWalletAsync(
            wallet.WalletId, new CreditWalletRequest(openingBalanceKobo, "opening balance"), CancellationToken.None);
        return wallet.WalletId;
    }
}
