namespace CommerceOps.Application.Triage;

public sealed class AiRiskAssessmentGuardrail
{
    private const int MaxTextLength = 2_000;

    public bool IsValid(AiRiskAssessmentRequest request, AiRiskAssessmentResult? result)
    {
        if (result is null || result.RiskScore is < 0 or > 100 || result.Confidence is < 0 or > 1)
        {
            return false;
        }

        var expectedLevel = result.RiskScore >= 70 ? "high" : result.RiskScore >= 30 ? "medium" : "low";
        if (!string.Equals(result.RiskLevel, expectedLevel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var allowedFinding = !string.IsNullOrWhiteSpace(result.PrimaryFindingCode) &&
            (request.Findings.Contains(result.PrimaryFindingCode, StringComparer.OrdinalIgnoreCase) ||
             result.PrimaryFindingCode is "stale_order" or "unknown");
        if (!allowedFinding)
        {
            return false;
        }

        var hasCriticalFinding = request.HasNegativeStock || request.Findings.Any(finding =>
            finding.Equals("negative_stock", StringComparison.OrdinalIgnoreCase) ||
            finding.Equals("order_paid_but_pending", StringComparison.OrdinalIgnoreCase));
        if (hasCriticalFinding && result.RiskScore < 30)
        {
            return false;
        }

        return TextIsSafe(result.Summary) &&
            TextIsSafe(result.ReasoningSummary) &&
            TextIsSafe(result.RecommendedAction) &&
            TextIsSafe(result.CustomerMessageSubject) &&
            TextIsSafe(result.CustomerMessageBody);
    }

    private static bool TextIsSafe(string? value) => value is null || value.Length <= MaxTextLength;
}
