# AsynchronousQueue — Демонстрация асинхронной обработки заказов

Учебный проект, демонстрирующий профессиональные паттерны асинхронной обработки на .NET 10:
**Transactional Outbox**, **RabbitMQ** через **MassTransit**, **Retry Policy**, **Dead Letter Queue** и живую статистику через Web UI.

---

## Содержание

- [Архитектура](#архитектура)
- [Технологии](#технологии)
- [Быстрый старт](#быстрый-старт)
- [Структура проекта](#структура-проекта)
- [Паттерны и решения](#паттерны-и-решения)
- [Web UI](#web-ui)
- [API](#api)
- [Конфигурация](#конфигурация)
- [Разработка](#разработка)

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────┐
│                        Web UI                               │
│  Форма запуска │ Live-слайдеры │ График очереди             │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP
┌────────────────────────▼────────────────────────────────────┐
│                    ASP.NET 10 API                           │
│                                                             │
│  POST /api/simulation/start                                 │
│  ┌──────────────────────────────────────┐                   │
│  │  SimulationService                   │                   │
│  │  Users + Orders + OutboxMessages     │                   │
│  │  ───── одна транзакция SQLite ─────  │                   │
│  └──────────────────────────────────────┘                   │
│                                                             │
│  OutboxDispatcher (IHostedService, каждые 200мс)           │
│  ┌──────────────────────────────────────┐                   │
│  │  Читает OutboxMessages (Published=0) │                   │
│  │  → Публикует в RabbitMQ             │                   │
│  │  → Помечает Published=1             │                   │
│  └──────────────────────┬───────────────┘                   │
└─────────────────────────│───────────────────────────────────┘
                          │ AMQP
┌─────────────────────────▼───────────────────────────────────┐
│                      RabbitMQ                               │
│   orders-queue          orders-queue_error (DLQ)            │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                   OrderConsumer                             │
│                                                             │
│  Normal distribution delay (Box-Muller)                    │
│  Spike mode (периодическое замедление)                      │
│                                                             │
│  Transient error → MassTransit Retry (3 попытки)           │
│  Business error  → Сразу Failed (без retry)                 │
│  Success         → Order.Status = Processed / Retried       │
└─────────────────────────────────────────────────────────────┘
```

### Поток данных

1. UI отправляет `POST /api/simulation/start`
2. `SimulationService` генерирует пользователей, заказы и `OutboxMessages` **в одной транзакции**
3. `OutboxDispatcher` каждые 200мс читает непубликованные сообщения и публикует в RabbitMQ
4. `OrderConsumer` обрабатывает сообщения с имитацией реальной нагрузки
5. UI каждую секунду опрашивает `/api/simulation/stats` и обновляет график

---

## Технологии

| Компонент | Технология |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal API |
| База данных | SQLite + Entity Framework Core 10 |
| Брокер сообщений | RabbitMQ 4 |
| Клиент MQ | MassTransit 8 |
| Контейнеризация | Docker + Docker Compose |
| API документация | Scalar (OpenAPI) |
| Web UI | Vanilla HTML/JS + Chart.js |

---

## Быстрый старт

### Требования

- Docker Desktop
- Docker Compose v2

### Запуск

```bash
git clone <repo>
cd AsynchronousQueue
docker-compose up --build
```

Сервисы будут доступны:

| Сервис | URL |
|---|---|
| Web UI | http://localhost:8080 |
| API документация (Scalar) | http://localhost:8080/scalar/v1 |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |

### Первый запуск

1. Открой http://localhost:8080
2. Задай параметры симуляции в левой панели (например: 10 пользователей, 5 заказов)
3. Нажми **Запустить**
4. Наблюдай как очередь наполняется (жёлтая линия) и опустошается (зелёная линия)
5. Меняй Live-слайдеры прямо во время обработки — Consumer реагирует немедленно

### Остановка

```bash
docker-compose down        # остановить, сохранить данные
docker-compose down -v     # остановить и удалить все данные (чистый старт)
```

---

## Структура проекта

```
AsynchronousQueue/
│
├── Domain/
│   ├── User.cs                    # Сущность пользователя
│   └── Order.cs                   # Сущность заказа (статусы: Pending → Processing → Processed/Retried/Failed)
│
├── Infrastructure/
│   ├── Db/
│   │   ├── AppDbContext.cs        # EF Core контекст, конфигурация индексов
│   │   └── OutboxMessage.cs       # Запись в таблице Outbox
│   ├── Messaging/
│   │   ├── OrderCreatedEvent.cs   # Контракт сообщения (immutable record)
│   │   ├── OrderConsumer.cs       # MassTransit Consumer
│   │   └── NormalDistribution.cs  # Box-Muller transform для реалистичного delay
│   └── OutboxDispatcher.cs        # IHostedService — polling Outbox каждые 200мс
│
├── Features/
│   └── Simulation/
│       ├── SimulationSettings.cs        # Параметры запуска (из конфига)
│       ├── ProcessingSettings.cs        # Параметры обработки (live)
│       ├── ProcessingSettingsHolder.cs  # Thread-safe хранилище live-настроек
│       ├── SimulationStateService.cs    # Singleton: состояние + скользящее окно processed/sec
│       ├── SimulationService.cs         # Генерация данных, Outbox транзакция, статистика
│       ├── SimulationModels.cs          # Request/Response records
│       └── SimulationEndpoints.cs       # Minimal API эндпоинты
│
├── wwwroot/
│   └── index.html                 # Single-page UI
│
├── Program.cs                     # Composition root
├── appsettings.json               # Конфигурация
├── Dockerfile                     # Multi-stage build
└── docker-compose.yml             # app + rabbitmq
```

---

## Паттерны и решения

### Transactional Outbox

Проблема: если сохранить заказ в БД и потом опубликовать в RabbitMQ — между этими операциями приложение может упасть, и сообщение будет потеряно.

Решение: сохраняем заказ и `OutboxMessage` **в одной транзакции**. `OutboxDispatcher` публикует сообщения отдельно.

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);
db.Users.AddRange(users);
db.Orders.AddRange(orders);
db.OutboxMessages.AddRange(outboxMessages); // ← в той же транзакции
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

**Гарантия at-least-once**: если приложение упадёт после публикации в RabbitMQ но до `SaveChanges` — сообщение будет опубликовано повторно. Consumer идемпотентен (проверяет `order is null`).

### Управление параллелизмом

Никаких ручных `lock`, `SemaphoreSlim` или `Mutex`. Параллелизм управляется MassTransit одной строкой:

```csharp
e.ConcurrentMessageLimit = simulationSettings.ConcurrentConsumers;
```

### Retry Policy

```csharp
e.UseMessageRetry(r => r.Incremental(
    retryLimit: 3,
    initialInterval: TimeSpan.FromMilliseconds(500),
    intervalIncrement: TimeSpan.FromMilliseconds(500)
));
// Паузы: 500мс → 1000мс → 1500мс
```

Два типа ошибок:
- **Transient** — бросает исключение → MassTransit делает retry → после 3 провалов уходит в DLQ (`orders-queue_error`)
- **Business** — сразу помечает `Order.Status = Failed` и возвращает без исключения → retry не происходит

### Live-настройки без перезапуска

`ProcessingSettingsHolder` хранит настройки как `volatile` ссылку. Замена атомарна через `Interlocked.Exchange`:

```csharp
public void Update(ProcessingSettings settings)
{
    Interlocked.Exchange(ref _current, settings);
}
```

Consumer читает `settingsHolder.Current` при каждом вызове — получает актуальные настройки мгновенно.

### Normal Distribution для delay

Реалистичнее равномерного `Random.Next(min, max)` — большинство заказов обрабатываются быстро, редкие "тяжёлые" — дольше. Реализован через **Box-Muller transform** без внешних библиотек:

```csharp
double u1 = 1.0 - rng.NextDouble();
double u2 = 1.0 - rng.NextDouble();
double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
return mean + stdDev * z;
```

### Spike Mode

Периодическое замедление — имитирует деградацию внешнего сервиса:

```csharp
var position = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % cfg.SpikeIntervalSeconds;
if (position < cfg.SpikeDurationSeconds)
    delayMs *= cfg.SpikeMultiplier;
```

---

## Web UI

### Левая панель — параметры

**Симуляция** (задаются перед запуском):
- Количество пользователей (лимит из конфига)
- Заказов на пользователя
- Повторений

**Обработка — Live** (применяются немедленно через `PUT /api/processing/settings`):
- Среднее время обработки (мс) — центр Normal distribution
- Разброс σ (мс) — ширина распределения
- Процент ошибок
- Тип ошибок: Transient / Business / Mixed

**Spike Mode**:
- Включить/выключить
- Интервал между спайками (с)
- Длительность спайка (с)
- Множитель задержки во время спайка

### Правая панель — статистика

**Основные метрики:**

| Метрика | Описание |
|---|---|
| Всего заказов | Сгенерировано в текущей симуляции |
| В очереди | Опубликовано в RabbitMQ, ещё не обработано |
| Обработано | Successful + Retried |
| Ошибки | Business errors в DLQ |

**Дополнительные метрики:**

| Метрика | Описание |
|---|---|
| Опубликовано | Прошло через OutboxDispatcher в RabbitMQ |
| Обрабатывается | Сейчас у Consumer (статус Processing) |
| После retry | Успешно обработано после Transient ошибки |
| Ожидают Outbox | Ещё не опубликованы диспетчером |
| Завершено % | (Processed + Retried + Failed) / Total |

**График** обновляется каждую секунду:
- 🟡 Жёлтая линия — `InQueue` (наполнение/опустошение очереди)
- 🟢 Зелёная линия — `Processed` накопительно
- 🔴 Красная линия — `Failed` накопительно

---

## API

Полная документация: http://localhost:8080/scalar/v1

### Симуляция

```http
POST /api/simulation/start
Content-Type: application/json

{
  "users": 10,
  "ordersPerUser": 5,
  "repetitions": 1
}
```

```http
GET /api/simulation/stats
```

```http
GET /api/simulation/config
```

```http
POST /api/simulation/reset
```

### Настройки обработки (live)

```http
GET /api/processing/settings
```

```http
PUT /api/processing/settings
Content-Type: application/json

{
  "delayMeanMs": 200,
  "delayStdDevMs": 100,
  "errorRatePercent": 5,
  "errorMode": "Mixed",
  "spikeModeEnabled": false,
  "spikeIntervalSeconds": 30,
  "spikeDurationSeconds": 5,
  "spikeMultiplier": 5
}
```

### Системные

```http
GET /health
```

---

## Конфигурация

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/app.db"
  },

  "RabbitMQ": {
    "Host": "rabbitmq",
    "VirtualHost": "/",
    "Username": "guest",
    "Password": "guest"
  },

  "Simulation": {
    "MaxUsers": 100,
    "MaxOrdersPerUser": 50,
    "MaxRepetitions": 10,
    "ConcurrentConsumers": 5,
    "OutboxPollingIntervalMs": 200
  },

  "Processing": {
    "DelayMeanMs": 200,
    "DelayStdDevMs": 100,
    "ErrorRatePercent": 5,
    "ErrorMode": "Mixed",
    "SpikeModeEnabled": false,
    "SpikeIntervalSeconds": 30,
    "SpikeDurationSeconds": 5,
    "SpikeMultiplier": 5
  }
}
```

### Параметры Simulation

| Параметр | Описание | По умолчанию |
|---|---|---|
| `MaxUsers` | Максимум пользователей за запуск | 100 |
| `MaxOrdersPerUser` | Максимум заказов на пользователя | 50 |
| `MaxRepetitions` | Максимум повторений | 10 |
| `ConcurrentConsumers` | Параллельных Consumer'ов | 5 |
| `OutboxPollingIntervalMs` | Интервал опроса Outbox | 200 |

### Параметры Processing

| Параметр | Описание | По умолчанию |
|---|---|---|
| `DelayMeanMs` | Среднее время обработки (мс) | 200 |
| `DelayStdDevMs` | Стандартное отклонение (мс) | 100 |
| `ErrorRatePercent` | Вероятность ошибки (%) | 5 |
| `ErrorMode` | Тип ошибок: Transient/Business/Mixed | Mixed |
| `SpikeModeEnabled` | Включить spike mode | false |
| `SpikeIntervalSeconds` | Интервал между спайками (с) | 30 |
| `SpikeDurationSeconds` | Длительность спайка (с) | 5 |
| `SpikeMultiplier` | Множитель задержки во время спайка | 5 |

### Пропускная способность

```
заказов/сек ≈ ConcurrentConsumers / DelayMeanMs × 1000
```

Примеры:
- 5 consumers × 200мс = **~25 заказов/сек**
- 10 consumers × 100мс = **~100 заказов/сек**
- 20 consumers × 50мс  = **~400 заказов/сек**

---

## Разработка

### Локальный запуск (без Docker)

Требуется локальный RabbitMQ. Измени `appsettings.Development.json`:

```json
{
  "RabbitMQ": {
    "Host": "localhost"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=orders_dev.db"
  }
}
```

```bash
dotnet run
```

### Полезные команды

```bash
# Пересобрать и запустить
docker-compose up --build

# Сбросить все данные (удалить volumes)
docker-compose down -v

# Логи приложения
docker-compose logs -f app

# Логи RabbitMQ
docker-compose logs -f rabbitmq

# Зайти в контейнер
docker exec -it asynchronousqueue-app-1 sh

# Посмотреть файлы в контейнере
docker exec asynchronousqueue-app-1 ls /app/wwwroot
```

### RabbitMQ Management UI

http://localhost:15672 (guest/guest)

Полезные разделы:
- **Queues** → `orders-queue` — глубина очереди в реальном времени
- **Queues** → `orders-queue_error` — Dead Letter Queue с failed сообщениями
- **Overview** → Message rates — скорость publish/consume

### Мониторинг через API

```bash
# Здоровье приложения
curl http://localhost:8080/health

# Текущая статистика
curl http://localhost:8080/api/simulation/stats | jq

# Текущие настройки обработки
curl http://localhost:8080/api/processing/settings | jq

# Запустить симуляцию
curl -X POST http://localhost:8080/api/simulation/start \
  -H "Content-Type: application/json" \
  -d '{"users":10,"ordersPerUser":5,"repetitions":1}'

# Увеличить нагрузку на Consumer
curl -X PUT http://localhost:8080/api/processing/settings \
  -H "Content-Type: application/json" \
  -d '{"delayMeanMs":500,"delayStdDevMs":200,"errorRatePercent":20,"errorMode":"Mixed","spikeModeEnabled":false,"spikeIntervalSeconds":30,"spikeDurationSeconds":5,"spikeMultiplier":5}'
```
