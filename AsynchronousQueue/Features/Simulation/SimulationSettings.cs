namespace AsynchronousQueue.Features.Simulation;

public sealed class SimulationSettings
{
    public const string SectionName = "Simulation";

    public int MaxUsers { get; set; }

    public int MaxOrdersPerUser { get; set; }

    public int MaxRepetitions { get; set; }

    public int ConsumerDelayMinMs { get; set; }

    public int ConsumerDelayMaxMs { get; set; }

    public int ErrorRatePercent { get; set; }

    public int OutboxPollingIntervalMs { get; set; }

    public int ConcurrentConsumers { get; set; }
}