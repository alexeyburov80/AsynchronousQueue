using System.Text.Json;
using AsynchronousQueue.Features.Simulation;
using AsynchronousQueue.Infrastructure.Db;
using AsynchronousQueue.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsynchronousQueue.Infrastructure;

/// <summary>
/// Фоновый сервис, реализующий паттерн Transactional Outbox.
///
/// Принцип работы:
///   1. Каждые OutboxPollingIntervalMs читает непубликованные записи из OutboxMessages
///   2. Публикует каждое сообщение в RabbitMQ через MassTransit
///   3. Помечает сообщение как Published и сохраняет в БД
///
/// Гарантия at-least-once: если приложение упадёт после публикации в RabbitMQ
/// но до SaveChanges — сообщение будет опубликовано повторно при следующем старте.
/// Consumer обязан быть идемпотентным (проверка order is null).
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IPublishEndpoint publisher,
    IOptions<SimulationSettings> settings,
    ILogger<OutboxDispatcher> logger
) : BackgroundService
{
    private readonly TimeSpan _pollingInterval =
        TimeSpan.FromMilliseconds(settings.Value.OutboxPollingIntervalMs);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher started (interval: {IntervalMs}ms)",
            _pollingInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Штатная остановка — выходим без лога ошибки
                break;
            }
            catch (Exception ex)
            {
                // Любая другая ошибка — логируем и продолжаем следующий цикл
                logger.LogError(ex, "OutboxDispatcher encountered an error");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        logger.LogInformation("OutboxDispatcher stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        // Новый scope на каждый цикл — DbContext scoped, OutboxDispatcher singleton
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await db.OutboxMessages
            .Where(m => !m.Published)
            .OrderBy(m => m.CreatedAt)
            .Take(100) // батч — не тянем всю таблицу разом
            .ToListAsync(ct);

        if (messages.Count == 0)
            return;

        logger.LogDebug("OutboxDispatcher: publishing {Count} messages", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload)
                    ?? throw new InvalidOperationException(
                        $"Cannot deserialize OutboxMessage {message.Id}");

                await publisher.Publish(evt, ct);

                message.Published = true;
                message.PublishedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Не прерываем батч — сообщение останется Unpublished и попадёт в след. цикл
                logger.LogError(ex, "Failed to publish OutboxMessage {MessageId}", message.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}