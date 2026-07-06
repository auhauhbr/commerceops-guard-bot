namespace CommerceOps.Application.Cases;

public sealed record CaseSummary(
    string CaseNumber,
    string Title,
    string RiskLevel,
    string Status);
