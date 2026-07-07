namespace CommerceOps.Application.Lumora;

public interface ILumoraClient
{
    Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraPaymentInconsistenciesResponse>> GetPaymentInconsistenciesAsync(
        CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraInventoryInconsistenciesResponse>> GetInventoryInconsistenciesAsync(
        CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraDatabaseIntegrityResponse>> GetDatabaseIntegrityAsync(
        CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraSlowQueriesResponse>> GetSlowQueriesAsync(
        CancellationToken cancellationToken = default);

    Task<LumoraClientResult<LumoraFailedJobsResponse>> GetFailedJobsAsync(
        CancellationToken cancellationToken = default);
}
