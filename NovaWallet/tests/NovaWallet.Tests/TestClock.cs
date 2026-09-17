using NovaWallet.Application.Interfaces;

namespace NovaWallet.Tests;

public class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
