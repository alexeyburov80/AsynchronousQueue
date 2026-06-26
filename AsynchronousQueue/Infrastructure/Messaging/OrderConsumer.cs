using AsynchronousQueue.Domain;
using AsynchronousQueue.Features.Simulation;
using AsynchronousQueue.Infrastructure.Db;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AsynchronousQueue.Infrastructure.Messaging;

/// <summary>
/// Потребитель сообщений OrderCreatedEvent из RabbitMQ.
///
/// Параллелизм управляется MassTransit (ConcurrentMessageLimit в Program.cs) —
/// никаких ручных lock или SemaphoreSlim не требуется.
///
/// ProcessingSettingsHolder читается при каждом вызове Consume(),
/// поэтому live-изменения из UI применяются немедленно.
/// </summary>
public sealed class OrderConsumer(
    AppDbContext db,
    ProcessingSettingsHolder settingsHolder,
    SimulationStateService simulationState,
    ILogger<OrderConsumer> logger
) : IConsumer<OrderCreatedEvent>
{
    private static readonly Random Rng = Random.Shared;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var evt = context.Message;
        var cfg = settingsHolder.Current; // актуальный снимок настроек

        logger.LogDebug("Processing order {OrderId}", evt.OrderId);

        var order = await db.Orders.FirstOrDefaultAsync(
            o => o.Id == evt.OrderId, context.CancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found, skipping", evt.OrderId);
            return;
        }

        order.Status = OrderStatus.Processing;
        await db.SaveChangesAsync(context.CancellationToken);

        // ── Имитация времени обработки (Normal distribution) ──────────────────
        var delayMs = NormalDistribution.SampleDelayMs(
            cfg.DelayMeanMs, cfg.DelayStdDevMs, Rng);

        if (cfg.SpikeModeEnabled && IsWithinSpike(cfg))
        {
            delayMs *= cfg.SpikeMultiplier;
            logger.LogDebug("Spike active, delay → {DelayMs}ms", delayMs);
        }

        await Task.Delay(delayMs, context.CancellationToken);

        // ── Имитация ошибок ───────────────────────────────────────────────────
        if (Rng.Next(100) < cfg.ErrorRatePercent)
        {
            var isBusiness = cfg.ErrorMode switch
            {
                ErrorMode.Business  => true,
                ErrorMode.Transient => false,
                ErrorMode.Mixed     => Rng.Next(100) < 30,
                _                   => false
            };

            if (isBusiness)
            {
                logger.LogWarning("Business error for order {OrderId}", evt.OrderId);
                order.Status = OrderStatus.Failed;
                order.ProcessedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(context.CancellationToken);
                simulationState.RecordProcessed();
                return;
            }

            // Transient error: исключение → MassTransit сделает retry
            logger.LogWarning("Transient error for order {OrderId}, attempt {Attempt}",
                evt.OrderId, context.GetRetryCount() + 1);

            throw new InvalidOperationException(
                $"Transient error for order {evt.OrderId}");
        }

        // ── Успех ─────────────────────────────────────────────────────────────
        order.Status = context.GetRetryCount() > 0
            ? OrderStatus.Retried
            : OrderStatus.Processed;
        order.ProcessedAt = DateTime.UtcNow;
        order.RetryCount = context.GetRetryCount();

        await db.SaveChangesAsync(context.CancellationToken);

        simulationState.RecordProcessed();

        logger.LogDebug("Order {OrderId} done in {DelayMs}ms (retries: {Retries})",
            evt.OrderId, delayMs, order.RetryCount);
    }

    /// <summary>
    /// Spike возникает периодически: каждые SpikeIntervalSeconds на SpikeDurationSeconds.
    /// </summary>
    private static bool IsWithinSpike(ProcessingSettings cfg)
    {
        var position = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % cfg.SpikeIntervalSeconds;
        return position < cfg.SpikeDurationSeconds;
    }
}