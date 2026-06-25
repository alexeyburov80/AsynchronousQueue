namespace AsynchronousQueue.Infrastructure.Messaging;

/// <summary>
/// Генератор случайных чисел с нормальным распределением.
/// Box-Muller transform — стандартный алгоритм без внешних зависимостей.
///
/// Нормальное распределение реалистично моделирует время обработки:
/// большинство заказов — быстро (вблизи mean), редкие "тяжёлые" — дольше.
/// </summary>
public static class NormalDistribution
{
    /// <summary>
    /// Возвращает случайное значение delay в миллисекундах.
    /// Зажато в [1ms, mean × 4] — исключает отрицательные и аномальные значения.
    /// </summary>
    public static int SampleDelayMs(double meanMs, double stdDevMs, Random rng)
    {
        const int MinMs = 1;
        const double MaxMultiplier = 4.0;

        var sample = BoxMuller(meanMs, stdDevMs, rng);
        var clamped = Math.Clamp(sample, MinMs, meanMs * MaxMultiplier);

        return (int)clamped;
    }

    /// <summary>
    /// Box-Muller transform: два равномерных числа → одно нормально распределённое.
    /// </summary>
    private static double BoxMuller(double mean, double stdDev, Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(); // (0, 1]
        double u2 = 1.0 - rng.NextDouble();
        double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

        return mean + stdDev * z;
    }
}