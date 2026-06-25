using AsynchronousQueue.Features.Simulation;
using AsynchronousQueue.Infrastructure.Db;
using AsynchronousQueue.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<SimulationSettings>(
    builder.Configuration.GetSection(SimulationSettings.SectionName));

// ProcessingSettingsHolder — singleton с live-настройками обработки
var initialProcessing = builder.Configuration
    .GetSection(ProcessingSettings.SectionName)
    .Get<ProcessingSettings>() ?? new ProcessingSettings();

builder.Services.AddSingleton(new ProcessingSettingsHolder(initialProcessing));

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── MassTransit + RabbitMQ ────────────────────────────────────────────────────
var simulationSettings = builder.Configuration
    .GetSection(SimulationSettings.SectionName)
    .Get<SimulationSettings>()!;

var rabbit = builder.Configuration.GetSection("RabbitMQ");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbit["Host"], rabbit["VirtualHost"], h =>
        {
            h.Username(rabbit["Username"]!);
            h.Password(rabbit["Password"]!);
        });

        cfg.ReceiveEndpoint("orders-queue", e =>
        {
            e.ConcurrentMessageLimit = simulationSettings.ConcurrentConsumers;

            e.UseMessageRetry(r => r.Incremental(
                retryLimit: 3,
                initialInterval: TimeSpan.FromMilliseconds(500),
                intervalIncrement: TimeSpan.FromMilliseconds(500)
            ));

            e.ConfigureConsumer<OrderConsumer>(context);
        });
    });
});

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Database init ─────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseStaticFiles();

// ── Processing settings API (live update) ────────────────────────────────────
app.MapGet("/api/processing/settings", (ProcessingSettingsHolder holder) =>
    Results.Ok(holder.Current));

app.MapPatch("/api/processing/settings", (
    System.Text.Json.JsonElement patch,
    ProcessingSettingsHolder holder) =>
{
    holder.Patch(patch);
    return Results.Ok(holder.Current);
});

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow
}));

app.Run();