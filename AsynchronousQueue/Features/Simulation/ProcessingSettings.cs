namespace AsynchronousQueue.Features.Simulation;

public enum ErrorMode
{
    Transient, // всегда успешно после retry
    Business,  // retry бесполезен → сразу в DLQ
    Mixed      // 70% Transient, 30% Business
}

public sealed class ProcessingSettings
{
    public const string SectionName = "Processing";

    // Задержка — Normal distribution
    public int DelayMeanMs { get; set; } = 200;
    public int DelayStdDevMs { get; set; } = 100;

    // Ошибки
    public int ErrorRatePercent { get; set; } = 5;
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Mixed;

    // Spike режим — периодическое замедление
    public bool SpikeModeEnabled { get; set; } = false;
    public int SpikeIntervalSeconds { get; set; } = 30;
    public int SpikeDurationSeconds { get; set; } = 5;
    public int SpikeMultiplier { get; set; } = 5;
}