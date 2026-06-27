using AsynchronousQueue.Infrastructure.Db;
using Microsoft.Extensions.Options;

namespace AsynchronousQueue.Features.Simulation;

public static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/simulation");

        // Запуск симуляции
        group.MapPost("/start", async (
            SimulationRequest request,
            SimulationService service,
            CancellationToken ct) =>
        {
            var stats = await service.StartAsync(request, ct);
            return Results.Ok(stats);
        });

        // Текущая статистика — polling из UI каждую секунду
        group.MapGet("/stats", async (
            SimulationService service,
            CancellationToken ct) =>
        {
            var stats = await service.GetStatsAsync(ct);
            return Results.Ok(stats);
        });

        // Лимиты из конфига — UI читает при загрузке, выставляет max на слайдерах
        group.MapGet("/config", (IOptions<SimulationSettings> settings) =>
            Results.Ok(new
            {
                settings.Value.MaxUsers,
                settings.Value.MaxOrdersPerUser,
                settings.Value.MaxRepetitions,
                settings.Value.ConcurrentConsumers
            }));

        group.MapPost("/reset", async (
            AppDbContext db,
            SimulationStateService state,
            CancellationToken ct) =>
        {
            await db.Database.EnsureDeletedAsync(ct);
            await db.Database.EnsureCreatedAsync(ct);
            state.MarkIdle();
            return Results.Ok(new { message = "Database reset" });
        });
        
        return app;
    }
}