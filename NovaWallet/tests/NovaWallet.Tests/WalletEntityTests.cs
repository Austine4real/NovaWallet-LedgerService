using NovaWallet.Domain.Entities;
using NovaWallet.Domain.Exceptions;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// Pure domain-level tests - no database involved. Wallet is a plain C#
/// class with no infrastructure dependency, so its invariants can (and
/// should) be tested in complete isolation and near-instantly.
/// </summary>
public class WalletEntityTests
{
    [Fact]
    public void Create_StartsAtZero()
    {
        var wallet = Wallet.Create("cust-001");
        Assert.Equal(0, wallet.BalanceKobo);
        Assert.Equal("NGN", wallet.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsInvalidCustomerId(string customerId)
    {
        Assert.Throws<ArgumentException>(() => Wallet.Create(customerId));
    }

    [Fact]
    public void Debit_MoreThanBalance_Throws_AndLeavesBalanceUnchanged()
    {
        var wallet = Wallet.Create("cust-001");
        wallet.Credit(100_00);

        Assert.Throws<InsufficientFundsException>(() => wallet.Debit(200_00));
        Assert.Equal(100_00, wallet.BalanceKobo); // unchanged after the failed attempt
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_NonPositiveAmount_Throws(long amount)
    {
        var wallet = Wallet.Create("cust-001");
        Assert.Throws<InvalidAmountException>(() => wallet.Credit(amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Debit_NonPositiveAmount_Throws(long amount)
    {
        var wallet = Wallet.Create("cust-001");
        Assert.Throws<InvalidAmountException>(() => wallet.Debit(amount));
    }

    [Fact]
    public void Credit_ThatWouldOverflowBalance_ThrowsInsteadOfWrappingNegative()
    {
        var wallet = Wallet.Create("cust-001");
        wallet.Credit(long.MaxValue - 1);

        // One more kobo pushes it past long.MaxValue - without `checked`
        // arithmetic here, this would silently wrap around to a large
        // negative balance instead of failing loudly. That silent wraparound
        // is exactly the "integer overflow as a route to a negative balance"
        // gap this test exists to close off.
        Assert.Throws<BalanceOverflowException>(() => wallet.Credit(10));
        Assert.Equal(long.MaxValue - 1, wallet.BalanceKobo); // unchanged after the failed attempt
    }
}
