using CommerceOps.Application.Cases;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class OperationalCaseQueryService(CommerceOpsDbContext dbContext) : IOperationalCaseQueryService
{
    private const string OpenStatus = "open";

    public async Task<IReadOnlyList<CaseSummary>> ListOpenCasesAsync(int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 50);

        return await dbContext.OperationalCases
            .AsNoTracking()
            .Where(operationalCase => operationalCase.Status == OpenStatus)
            .OrderByDescending(operationalCase => operationalCase.CaseNumber)
            .Take(normalizedLimit)
            .Select(operationalCase => new CaseSummary(
                operationalCase.CaseNumber,
                operationalCase.Title,
                operationalCase.RiskLevel,
                operationalCase.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<CaseDetails?> GetCaseByNumberAsync(string caseNumber, CancellationToken cancellationToken)
    {
        var normalizedCaseNumber = caseNumber.Trim().ToUpperInvariant();

        return await dbContext.OperationalCases
            .AsNoTracking()
            .Where(operationalCase => operationalCase.CaseNumber.ToUpper() == normalizedCaseNumber)
            .Select(operationalCase => new CaseDetails(
                operationalCase.CaseNumber,
                operationalCase.Title,
                operationalCase.Summary,
                operationalCase.RiskLevel,
                operationalCase.Status,
                operationalCase.EntityType,
                operationalCase.EntityId))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
