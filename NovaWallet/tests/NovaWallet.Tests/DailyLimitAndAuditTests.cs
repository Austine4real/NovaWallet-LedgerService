using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.DTOs;
using NovaWallet.Application.Services;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// Tests for behaviour that lives partly or entirely in the database itself
/// (the WAT-midnight daily limit reset, and the audit-log immutability
/// trigger added by the HardenLedgerInvariants migration) - these can't be
/// verified by reading the C# alone, so they're worth testing explicitly
/// rather than just documenting the intended behaviour in the README.
/// </summary>
public class DailyLimitAndAuditTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DailyLimitAndAuditTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DailyLimit_ResetsAtMidnightWAT()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        // 22:00 UTC on 17 Sept = 23:00 WAT (UTC+1) - still 17 Sept in WAT.
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 9, 17, 22, 0, 0, TimeSpan.Zero) };
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromCustomerId = $"cust-{Guid.NewGuid():N}";
        var fromWallet = await walletService.CreateWalletAsync(new CreateWalletRequest(fromCustomerId), fromCustomerId, CancellationToken.None);
        await walletService.CreditWalletAsync(
            fromWallet.WalletId, new CreditWalletRequest(TransferService.DailyLimitKobo * 2, "opening balance"), fromCustomerId, CancellationToken.None);

        var toCustomerId = $"cust-{Guid.NewGuid():N}";
        var toWallet = await walletService.CreateWalletAsync(new CreateWalletRequest(toCustomerId), toCustomerId, CancellationToken.None);

        // Use the full daily limit while it's still 17 Sept in WAT.
        await transferService.TransferAsync(
            new TransferRequest(fromWallet.WalletId, toWallet.WalletId, TransferService.DailyLimitKobo, "day 1"),
            Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None);

        // A second transfer the same WAT day should now breach the limit.
        await Assert.ThrowsAsync<DailyLimitExceededException>(() => transferService.TransferAsync(
            new TransferRequest(fromWallet.WalletId, toWallet.WalletId, 1_00, "still day 1"),
            Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None));

        // Push forward 2 hours: 00:00 UTC on 18 Sept = 01:00 WAT on 18 Sept -
        // now a new WAT calendar day.
        clock.UtcNow = clock.UtcNow.AddHours(2);

        // The same wallet can send the full limit again, since the daily
        // limit is computed per WAT calendar date, not a rolling 24 hours.
        var secondDayResult = await transferService.TransferAsync(
            new TransferRequest(fromWallet.WalletId, toWallet.WalletId, TransferService.DailyLimitKobo, "day 2"),
            Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None);

        Assert.Equal(0, secondDayResult.FromWalletBalanceAfterKobo);
    }

    [Fact]
    public async Task AuditLog_DatabaseRejectsUpdate()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);

        var customerId = $"cust-{Guid.NewGuid():N}";
        var wallet = await walletService.CreateWalletAsync(new CreateWalletRequest(customerId), customerId, CancellationToken.None);
        await walletService.CreditWalletAsync(
            wallet.WalletId, new CreditWalletRequest(100_00, "generates an audit row"), customerId, CancellationToken.None);

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.WalletId == wallet.WalletId);

        // The HardenLedgerInvariants migration adds an AFTER UPDATE, DELETE
        // trigger on AuditLogEntries that THROWs unconditionally. This test
        // bypasses the application layer entirely (no repository call - the
        // real IAuditLogRepository only exposes Add()) to prove the
        // enforcement is real at the database level, not just something the
        // app happens to never call.
        var ex = await Record.ExceptionAsync(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditLogEntries SET Description = 'tampered' WHERE Id = {entry.Id}"));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task FailedTransfer_DoesNotAppendAuditRows()
    {
        await using var uow = _fixture.CreateUnitOfWork();
        var clock = new TestClock();
        var walletService = new WalletService(uow, clock);
        var transferService = new TransferService(uow, clock);

        var fromCustomerId = $"cust-{Guid.NewGuid():N}";
        var fromWallet = await walletService.CreateWalletAsync(new CreateWalletRequest(fromCustomerId), fromCustomerId, CancellationToken.None);
        await walletService.CreditWalletAsync(
            fromWallet.WalletId, new CreditWalletRequest(50_00, "not enough"), fromCustomerId, CancellationToken.None);

        var toCustomerId = $"cust-{Guid.NewGuid():N}";
        var toWallet = await walletService.CreateWalletAsync(new CreateWalletRequest(toCustomerId), toCustomerId, CancellationToken.None);

        await using var verifyDb = _fixture.CreateContext();
        var auditCountBefore = await verifyDb.AuditLogEntries
            .CountAsync(a => a.WalletId == fromWallet.WalletId || a.WalletId == toWallet.WalletId);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => transferService.TransferAsync(
            new TransferRequest(fromWallet.WalletId, toWallet.WalletId, 200_00, "too much"),
            Guid.NewGuid().ToString(), fromCustomerId, CancellationToken.None));

        var auditCountAfter = await verifyDb.AuditLogEntries
            .CountAsync(a => a.WalletId == fromWallet.WalletId || a.WalletId == toWallet.WalletId);

        Assert.Equal(auditCountBefore, auditCountAfter);
    }
}
