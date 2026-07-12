namespace CommerceOps.Application.Triage;

public sealed class DeterministicOrderRiskClassifier(IOrderRiskScorer riskScorer) : IOrderRiskClassifier
{
    public Task<AiRiskAssessmentResult> ClassifyAsync(
        OrderTriageCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var score = riskScorer.Score(candidate, now);
        return Task.FromResult(new AiRiskAssessmentResult(
            score.Level,
            score.Score,
            score.PrimaryFindingCode,
            score.Summary,
            null,
            null,
            null,
            null,
            1,
            "deterministic"));
    }
}
