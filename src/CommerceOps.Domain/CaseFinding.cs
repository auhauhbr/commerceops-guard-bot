namespace CommerceOps.Domain;

public sealed class CaseFinding
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public OperationalCase? Case { get; set; }
    public required string Type { get; set; }
    public required string Severity { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string EvidenceJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
