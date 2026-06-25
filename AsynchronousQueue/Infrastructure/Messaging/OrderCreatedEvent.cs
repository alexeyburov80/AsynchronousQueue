namespace AsynchronousQueue.Infrastructure.Messaging;

/// <summary>
/// Контракт сообщения, публикуемого в RabbitMQ для каждого созданного заказа.
/// Immutable record — контракт не должен меняться после публикации.
/// </summary>
public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid UserId,
    DateTime CreatedAt
);