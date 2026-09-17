using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Domain.Entities;

public class Wallet
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public long BalanceKobo { get; private set; }
    public string Currency { get; private set; } = "NGN";
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core materializes via this parameterless constructor.
    private Wallet() { }

    public static Wallet Create(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Customer id is required.", nameof(customerId));

        return new Wallet
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId.Trim(),
            BalanceKobo = 0,
            Currency = "NGN",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Credit(long amountKobo)
    {
        if (amountKobo <= 0) throw new InvalidAmountException(amountKobo);

        // `checked` turns a silent overflow (which would wrap BalanceKobo
        // around to a large negative number) into a clean, catchable
        // exception instead. At realistic Naira balances this is
        // astronomically unlikely to trigger - long.MaxValue kobo is on the
        // order of tens of quadrillions of Naira - but a ledger should never
        // rely on "unlikely" for a correctness guarantee.
        checked
        {
            try
            {
                BalanceKobo += amountKobo;
            }
            catch (OverflowException)
            {
                throw new BalanceOverflowException(Id);
            }
        }
    }

    public void Debit(long amountKobo)
    {
        if (amountKobo <= 0) throw new InvalidAmountException(amountKobo);
        if (BalanceKobo < amountKobo) throw new InsufficientFundsException(Id, BalanceKobo, amountKobo);
        BalanceKobo -= amountKobo;
    }
}
