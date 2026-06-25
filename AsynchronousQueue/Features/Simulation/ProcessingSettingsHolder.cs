using System.Text.Json;
using System.Text.Json.Nodes;

namespace AsynchronousQueue.Features.Simulation;

/// <summary>
/// Хранит актуальные ProcessingSettings в памяти.
/// Позволяет обновлять настройки вживую через API без перезапуска.
///
/// Потокобезопасность: ссылка на объект заменяется атомарно через Interlocked,
/// поэтому Consumer всегда читает консистентный снимок настроек.
/// </summary>
public sealed class ProcessingSettingsHolder
{
    private volatile ProcessingSettings _current;

    public ProcessingSettingsHolder(ProcessingSettings initial)
    {
        _current = initial;
    }

    /// <summary>Получить текущие настройки. Потокобезопасно, без блокировок.</summary>
    public ProcessingSettings Current => _current;

    /// <summary>
    /// Заменить настройки целиком. Следующий вызов Current вернёт новый объект.
    /// volatile гарантирует видимость изменения для всех потоков.
    /// </summary>
    public void Update(ProcessingSettings settings)
    {
        Interlocked.Exchange(ref _current, settings);
    }

    /// <summary>Обновить отдельные поля через JSON patch.</summary>
    public void Patch(JsonElement patch)
    {
        var currentJson = JsonSerializer.SerializeToNode(_current)!.AsObject();

        foreach (var prop in patch.EnumerateObject())
            currentJson[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());

        var updated = JsonSerializer.Deserialize<ProcessingSettings>(
            currentJson.ToJsonString())!;
        Update(updated);
    }
}