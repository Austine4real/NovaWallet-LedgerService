using NovaWallet.Application.Interfaces;

namespace NovaWallet.Infrastructure;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
