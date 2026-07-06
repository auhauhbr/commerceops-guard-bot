using CommerceOps.Application.Cases;
using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class CaseService(CommerceOpsDbContext dbContext, CaseRuleEvaluator ruleEvaluator, TimeProvider timeProvider)
    : ICaseService
{
    private const string OpenStatus = "open";

    public async Task EvaluateOperationalEventAsync(OperationalEvent operationalEvent, CancellationToken cancellationToken)
    {
        var candidate = ruleEvaluator.Evaluate(operationalEvent);
        if (candidate is null)
        {
            return;
        }

        var duplicateExists = await dbContext.OperationalCases.AnyAsync(
            operationalCase =>
                operationalCase.ClientApplicationId == operationalEvent.ClientApplicationId &&
                operationalCase.EntityType == operationalEvent.EntityType &&
                operationalCase.EntityId == operationalEvent.EntityId &&
                operationalCase.ProblemType == candidate.ProblemType &&
                operationalCase.Status == OpenStatus,
            cancellationToken);

        if (duplicateExists)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var operationalCase = new OperationalCase
        {
            Id = Guid.NewGuid(),
            CaseNumber = await CreateCaseNumberAsync(cancellationToken),
            ClientApplicationId = operationalEvent.ClientApplicationId,
            ProblemType = candidate.ProblemType,
            Title = candidate.Title,
            Summary = candidate.Summary,
            Status = OpenStatus,
            RiskLevel = candidate.RiskLevel,
            RiskScore = candidate.RiskScore,
            EntityType = operationalEvent.EntityType,
            EntityId = operationalEvent.EntityId,
            CreatedAt = now,
            UpdatedAt = now,
            Findings =
            [
                new CaseFinding
                {
                    Id = Guid.NewGuid(),
                    Type = candidate.FindingType,
                    Severity = candidate.FindingSeverity,
                    Title = candidate.FindingTitle,
                    Description = candidate.FindingDescription,
                    EvidenceJson = candidate.EvidenceJson,
                    CreatedAt = now
                }
            ]
        };

        dbContext.OperationalCases.Add(operationalCase);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> CreateCaseNumberAsync(CancellationToken cancellationToken)
    {
        var nextNumber = await dbContext.OperationalCases.CountAsync(cancellationToken) + 1;
        return $"CASE-{nextNumber:00000}";
    }
}
