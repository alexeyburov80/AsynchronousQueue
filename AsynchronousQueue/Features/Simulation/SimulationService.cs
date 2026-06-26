using System.Text.Json;
using AsynchronousQueue.Domain;
using AsynchronousQueue.Infrastructure.Db;
using AsynchronousQueue.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsynchronousQueue.Features.Simulation;

/// <summary>
/// Генерирует тестовые данные и запускает симуляцию.
///
/// Пользователи + заказы + OutboxMessages сохраняются в одной транзакции —
/// это и есть суть паттерна Transactional Outbox: либо всё вместе, либо ничего.
/// OutboxDispatcher подхватит сообщения и опубликует в RabbitMQ.
/// </summary>
public sealed class SimulationService(
    AppDbContext db,
    SimulationStateService state,
    IOptions<SimulationSettings> settings,
    ILogger<SimulationService> logger
)
{
    private static readonly Random Rng = Random.Shared;

    public async Task<SimulationStats> StartAsync(SimulationRequest request, CancellationToken ct)
    {
        var cfg = settings.Value;

        var users        = Math.Clamp(request.Users, 1, cfg.MaxUsers);
        var ordersPerUser = Math.Clamp(request.OrdersPerUser, 1, cfg.MaxOrdersPerUser);
        var repetitions  = Math.Clamp(request.Repetitions, 1, cfg.MaxRepetitions);

        logger.LogInformation(
            "Starting simulation: {Users} users × {Orders} orders × {Reps} repetitions",
            users, ordersPerUser, repetitions);

        state.MarkRunning();

        var totalOrders = 0;

        for (var rep = 0; rep < repetitions; rep++)
            totalOrders += await GenerateBatchAsync(users, ordersPerUser, ct);

        logger.LogInformation("Simulation enqueued {Total} orders", totalOrders);

        return await GetStatsAsync(ct);
    }

    private async Task<int> GenerateBatchAsync(int userCount, int ordersPerUser, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var users = Enumerable.Range(0, userCount)
            .Select(_ => new User
            {
                Id        = Guid.NewGuid(),
                Name      = $"User-{Guid.NewGuid():N}".Substring(0, 16),
                CreatedAt = now
            })
            .ToList();

        var orders = users.SelectMany(user =>
            Enumerable.Range(0, ordersPerUser).Select(_ => new Order
            {
                Id          = Guid.NewGuid(),
                UserId      = user.Id,
                Description = $"Order-{Guid.NewGuid():N}".Substring(0, 18),
                Amount      = Math.Round((decimal)(Rng.NextDouble() * 1000), 2),
                Status      = OrderStatus.Pending,
                CreatedAt   = now
            })
        ).ToList();

        var outboxMessages = orders.Select(order => new OutboxMessage
        {
            OrderId   = order.Id,
            Payload   = JsonSerializer.Serialize(
                new OrderCreatedEvent(order.Id, order.UserId, order.CreatedAt)),
            Published = false,
            CreatedAt = now
        }).ToList();

        // Одна транзакция — Transactional Outbox
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Users.AddRange(users);
        db.Orders.AddRange(orders);
        db.OutboxMessages.AddRange(outboxMessages);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return orders.Count;
    }

    public async Task<SimulationStats> GetStatsAsync(CancellationToken ct)
    {
        var statusCounts = await db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var processing = statusCounts.FirstOrDefault(x => x.Status == OrderStatus.Processing)?.Count ?? 0;
        var processed  = statusCounts.FirstOrDefault(x => x.Status == OrderStatus.Processed)?.Count ?? 0;
        var retried    = statusCounts.FirstOrDefault(x => x.Status == OrderStatus.Retried)?.Count ?? 0;
        var failed     = statusCounts.FirstOrDefault(x => x.Status == OrderStatus.Failed)?.Count ?? 0;
        var totalOrders = statusCounts.Sum(x => x.Count);

        var publishedCount = await db.OutboxMessages.CountAsync(m => m.Published, ct);

        // InQueue = опубликовано в RabbitMQ, но ещё не обработано Consumer'ом
        var doneCount = processed + retried + failed;
        var inQueue   = Math.Max(0, publishedCount - doneCount);

        var perSecond = state.GetProcessedPerSecond();
        var isRunning = state.IsRunning && (totalOrders - doneCount) > 0;

        if (!isRunning)
            state.MarkIdle();

        return new SimulationStats(
            TotalOrders:        totalOrders,
            Published:          publishedCount,
            InQueue:            inQueue,
            Processing:         processing,
            Processed:          processed,
            Retried:            retried,
            Failed:             failed,
            ProcessedPerSecond: perSecond,
            IsRunning:          isRunning
        );
    }
}