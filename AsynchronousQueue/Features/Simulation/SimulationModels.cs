namespace AsynchronousQueue.Features.Simulation;

public sealed record SimulationRequest(
    int Users,
    int OrdersPerUser,
    int Repetitions
);

public sealed record SimulationStats(
    int TotalOrders,
    int Published,
    int InQueue,
    int Processing,
    int Processed,
    int Retried,
    int Failed,
    int ProcessedPerSecond,
    bool IsRunning
);