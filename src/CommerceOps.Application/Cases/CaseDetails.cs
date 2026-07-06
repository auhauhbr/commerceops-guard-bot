namespace CommerceOps.Application.Cases;

public sealed record CaseDetails(
    string CaseNumber,
    string Title,
    string Summary,
    string RiskLevel,
    string Status,
    string EntityType,
    string EntityId);
