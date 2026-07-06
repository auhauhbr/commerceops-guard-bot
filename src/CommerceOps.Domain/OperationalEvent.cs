namespace CommerceOps.Domain;

public sealed class OperationalEvent
{
    public Guid Id { get; set; }
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }
    public required string EventType { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Severity { get; set; }
    public required string RawBody { get; set; }
    public string? DataJson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
