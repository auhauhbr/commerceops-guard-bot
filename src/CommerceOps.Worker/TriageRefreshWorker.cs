using CommerceOps.Application.Triage;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommerceOps.Worker;

public sealed class TriageRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TriageRefreshOptions> options,
    ILogger<TriageRefreshWorker> logger) : BackgroundService
{
    private readonly TriageRefreshOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Order triage refresh worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        await RefreshOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
            var triageService = scope.ServiceProvider.GetRequiredService<IOrderTriageService>();

            var clientApplication = await dbContext.ClientApplications
                .AsNoTracking()
                .Where(application => application.IsActive)
                .SingleOrDefaultAsync(
                    application => application.PublicId == _options.ClientApplicationPublicId,
                    cancellationToken);

            if (clientApplication is null)
            {
                logger.LogWarning(
                    "Order triage refresh skipped because client application {ClientApplicationPublicId} is not configured or inactive.",
                    _options.ClientApplicationPublicId);
                return;
            }

            logger.LogInformation(
                "Starting order triage refresh for client application {ClientApplicationPublicId}.",
                clientApplication.PublicId);

            var result = await triageService.RefreshAsync(
                clientApplication.Id,
                _options.LookbackMinutes,
                _options.Limit,
                cancellationToken);

            if (!result.IsSuccess)
            {
                logger.LogError(
                    "Order triage refresh failed with error code {ErrorCode}.",
                    result.ErrorCode);
                return;
            }

            logger.LogInformation(
                "Order triage refresh completed. Candidates: {CandidatesCount}. Upserted: {UpsertedCount}. Skipped: {SkippedCount}.",
                result.CandidatesCount,
                result.UpsertedCount,
                result.SkippedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order triage refresh failed.");
        }
    }
}

