namespace NovaWallet.Application.Interfaces;

/// <summary>Thin abstraction over UtcNow so time can be controlled in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
