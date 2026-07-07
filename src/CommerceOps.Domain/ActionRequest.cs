namespace CommerceOps.Domain;

public sealed class ActionRequest
{
    public Guid Id { get; set; }
    public required string PublicId { get; set; }
    public required string Type { get; set; }
    public required string Status { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? Risk { get; set; }
    public required string Reason { get; set; }
    public required string PayloadJson { get; set; }
    public long CreatedByChatId { get; set; }
    public long? ApprovedByChatId { get; set; }
    public long? CancelledByChatId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public string? FailureReason { get; set; }
}
