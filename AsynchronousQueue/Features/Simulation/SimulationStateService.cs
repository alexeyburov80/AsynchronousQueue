namespace AsynchronousQueue.Features.Simulation;

/// <summary>
/// Singleton-сервис, отслеживающий состояние симуляции и скорость обработки.
///
/// ProcessedPerSecond считается через скользящее окно (5 секунд):
/// Consumer вызывает RecordProcessed() после каждого успешного заказа,
/// GetProcessedPerSecond() возвращает среднее за последние 5 секунд.
/// </summary>
public sealed class SimulationStateService
{
    private volatile bool _isRunning;

    private readonly Queue<DateTime> _processedTimestamps = new();
    private readonly Lock _lock = new();
    private readonly TimeSpan _window = TimeSpan.FromSeconds(5);

    public bool IsRunning => _isRunning;

    public void MarkRunning() => _isRunning = true;
    public void MarkIdle()    => _isRunning = false;

    /// <summary>Вызывается Consumer'ом после успешной обработки заказа.</summary>
    public void RecordProcessed()
    {
        lock (_lock)
        {
            _processedTimestamps.Enqueue(DateTime.UtcNow);
        }
    }

    /// <summary>Количество обработанных заказов в секунду (скользящее окно 5с).</summary>
    public int GetProcessedPerSecond()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - _window;

            while (_processedTimestamps.Count > 0 && _processedTimestamps.Peek() < cutoff)
                _processedTimestamps.Dequeue();

            return (int)(_processedTimestamps.Count / _window.TotalSeconds);
        }
    }
}