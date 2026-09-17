using Microsoft.EntityFrameworkCore;
using NovaWallet.Infrastructure.Persistence;

namespace NovaWallet.Tests;

/// <summary>
/// These tests run against a real SQL Server instance because the
/// concurrency guarantees under test (UPDLOCK/ROWLOCK row locking) are a
/// property of the database engine, not something an in-memory provider can
/// faithfully emulate. Start the database first:
///
///   docker compose up -d db
///   dotnet ef database update --project src/NovaWallet.Infrastructure --startup-project src/NovaWallet.Api
///   dotnet test
///
/// The connection string can be overridden via the NOVAWALLET_TEST_DB
/// environment variable if you're not using the default docker-compose setup.
/// </summary>
public class DatabaseFixture
{
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("NOVAWALLET_TEST_DB")
        ?? "Server=localhost,1433;Database=NovaWallet;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

    public NovaWalletDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new NovaWalletDbContext(options);
    }

    /// <summary>
    /// A fresh UnitOfWork (and its own underlying DbContext) per call - tests
    /// that simulate concurrent requests call this once per simulated
    /// request, exactly mirroring the "one DbContext per HTTP request"
    /// lifetime used in the running API (see Program.cs's AddScoped
    /// registrations).
    /// </summary>
    public UnitOfWork CreateUnitOfWork() => new(CreateContext());
}

