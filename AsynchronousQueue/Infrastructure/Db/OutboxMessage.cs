namespace AsynchronousQueue.Infrastructure.Db;

public class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool Published { get; set; }

    public DateTime? PublishedAt { get; set; }
}