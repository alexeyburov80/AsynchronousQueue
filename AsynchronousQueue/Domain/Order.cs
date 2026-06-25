namespace AsynchronousQueue.Domain;

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Retried = 3,
    Failed = 4
}

public class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public int RetryCount { get; set; }
}