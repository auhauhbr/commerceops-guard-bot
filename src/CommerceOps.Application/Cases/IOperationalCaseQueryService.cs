namespace CommerceOps.Application.Cases;

public interface IOperationalCaseQueryService
{
    Task<IReadOnlyList<CaseSummary>> ListOpenCasesAsync(int limit, CancellationToken cancellationToken);

    Task<CaseDetails?> GetCaseByNumberAsync(string caseNumber, CancellationToken cancellationToken);
}
