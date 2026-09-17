namespace NovaWallet.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public sealed class WalletNotFoundException : DomainException
{
    public Guid WalletId { get; }
    public WalletNotFoundException(Guid walletId)
        : base($"Wallet '{walletId}' was not found.") => WalletId = walletId;
}

public sealed class CustomerAlreadyHasWalletException : DomainException
{
    public string CustomerId { get; }
    public Guid WalletId { get; }
    public CustomerAlreadyHasWalletException(string customerId, Guid walletId)
        : base($"Customer '{customerId}' already has a wallet.")
    {
        CustomerId = customerId;
        WalletId = walletId;
    }
}

public sealed class InsufficientFundsException : DomainException
{
    public Guid WalletId { get; }
    public long BalanceKobo { get; }
    public long RequestedKobo { get; }

    public InsufficientFundsException(Guid walletId, long balanceKobo, long requestedKobo)
        : base($"Wallet '{walletId}' has insufficient funds. Balance: {balanceKobo}, requested: {requestedKobo}.")
    {
        WalletId = walletId;
        BalanceKobo = balanceKobo;
        RequestedKobo = requestedKobo;
    }
}

public sealed class DailyLimitExceededException : DomainException
{
    public Guid WalletId { get; }
    public long DailyLimitKobo { get; }

    public DailyLimitExceededException(Guid walletId, long dailyLimitKobo)
        : base($"Wallet '{walletId}' would exceed the daily outbound transfer limit of {dailyLimitKobo} kobo.")
    {
        WalletId = walletId;
        DailyLimitKobo = dailyLimitKobo;
    }
}

public sealed class InvalidTransferException : DomainException
{
    public InvalidTransferException(string message) : base(message) { }
}

public sealed class InvalidAmountException : DomainException
{
    public InvalidAmountException(long amountKobo)
        : base($"Amount must be a positive number of kobo. Received: {amountKobo}.") { }
}

public sealed class IdempotencyKeyConflictException : DomainException
{
    public string IdempotencyKey { get; }
    public IdempotencyKeyConflictException(string idempotencyKey)
        : base($"Idempotency-Key '{idempotencyKey}' was already used with a different request payload.")
        => IdempotencyKey = idempotencyKey;
}

public sealed class IdempotencyKeyMissingException : DomainException
{
    public IdempotencyKeyMissingException() : base("An Idempotency-Key header is required for this operation.") { }
}

public sealed class BalanceOverflowException : DomainException
{
    public BalanceOverflowException(Guid walletId)
        : base($"Crediting wallet '{walletId}' would overflow the maximum representable balance.") { }
}

/// <summary>
/// Raised when an authenticated caller attempts an operation against a wallet
/// they do not own. Deliberately a distinct type from any 401-mapped
/// exception: this is "you are who you say you are, but you can't do this,"
/// not "you aren't authenticated at all."
/// </summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}
