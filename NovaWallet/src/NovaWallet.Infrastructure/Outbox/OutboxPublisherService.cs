using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaWallet.Application.Interfaces;

namespace NovaWallet.Infrastructure.Outbox;

/// <summary>
/// Polls the outbox table for unpublished events and publishes them.
/// "Publishing" here means structured logging - this exercise has no real
/// message broker to publish to, so this simulates the delivery step while
/// exercising the actual outbox mechanics (poll, publish, mark-published)
/// that would carry over unchanged if a real broker were plugged in later.
///
/// Runs as an IHostedService with its own DI scope per poll cycle, since
/// NovaWalletDbContext (and therefore IUnitOfWork) is registered as scoped,
/// but this service itself lives for the lifetime of the application.
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox publisher iteration failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        // CreateAsyncScope (not CreateScope) is required here: UnitOfWork
        // only implements IAsyncDisposable (it wraps a DbContext, which must
        // be disposed asynchronously), so the scope itself must be disposed
        // asynchronously too - a synchronous `using var scope = CreateScope()`
        // throws when the container tries to dispose an async-only service
        // synchronously at scope teardown.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pending = await uow.Outbox.GetUnpublishedAsync(BatchSize, ct);
        if (pending.Count == 0) return;

        foreach (var message in pending)
        {
            // In production this would push to a real broker (Azure Service
            // Bus, Kafka, etc.). Logged here for the exercise instead.
            _logger.LogInformation(
                "Publishing outbox event {MessageId} of type {Type}: {Payload}",
                message.Id, message.Type, message.Payload);

            uow.Outbox.MarkPublished(message, DateTimeOffset.UtcNow);
        }

        await uow.SaveChangesAsync(ct);
    }
}
