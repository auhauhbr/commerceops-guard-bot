namespace CommerceOps.Application.Cases;

public sealed record CaseCreationCandidate(
    string ProblemType,
    string Title,
    string Summary,
    string RiskLevel,
    int RiskScore,
    string FindingType,
    string FindingSeverity,
    string FindingTitle,
    string FindingDescription,
    string EvidenceJson);
