using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tezgah.Core.Messaging;

/// <summary>Delivers the outbox of one module. One instance runs per module <typeparamref name="TContext"/>.</summary>
public sealed class OutboxProcessor<TContext>(
    IServiceScopeFactory scopes,
    IntegrationEventDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        do
        {
            try
            {
                while (await ProcessBatchAsync(stoppingToken) == _options.BatchSize)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox polling failed for {Context}", typeof(TContext).Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        // The table name comes from the EF model (each module keeps its outbox in its own schema), not from input.
        var table = db.Model.FindEntityType(typeof(OutboxMessage))!.GetSchemaQualifiedTableName();
        var nextMessages = $$"""
            SELECT * FROM {{table}}
            WHERE processed_at IS NULL AND attempts < {0}
            ORDER BY occurred_at
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

        // The row locks are held until the batch commits. SKIP LOCKED lets other instances of the
        // application take the next rows instead of waiting for, or re-delivering, these ones.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(nextMessages, _options.MaxAttempts, _options.BatchSize)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await dispatcher.DispatchAsync(message.Type, message.Payload, ct);
                message.MarkProcessed(time.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Delivery of outbox message {MessageId} ({Type}) failed, attempt {Attempt}",
                    message.Id, message.Type, message.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }
}
