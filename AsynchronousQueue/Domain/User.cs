namespace AsynchronousQueue.Domain;

public class User
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Order> Orders { get; set; } = [];
}