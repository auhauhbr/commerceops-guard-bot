namespace CommerceOps.Domain;

public sealed class OperationalCase
{
    public Guid Id { get; set; }
    public required string CaseNumber { get; set; }
    public Guid ClientApplicationId { get; set; }
    public ClientApplication? ClientApplication { get; set; }
    public required string ProblemType { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Status { get; set; }
    public required string RiskLevel { get; set; }
    public int RiskScore { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public List<CaseFinding> Findings { get; set; } = [];
}
