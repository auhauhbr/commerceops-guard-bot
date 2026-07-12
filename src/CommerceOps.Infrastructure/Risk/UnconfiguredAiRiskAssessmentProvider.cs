using CommerceOps.Application.Triage;

namespace CommerceOps.Infrastructure.Risk;

// Extension point only. No external AI request is made in this phase.
public sealed class UnconfiguredAiRiskAssessmentProvider : IAiRiskAssessmentProvider
{
    public Task<string> AssessAsync(
        AiRiskAssessmentRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No AI risk provider is configured.");
}
