using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;
using CommerceOps.Domain;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.IntegrationTests;

public sealed class OrderTriageServiceTests
{
    [Fact]
    public async Task RefreshPassesLookbackAndLimitToLumoraClient()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CommerceOpsDbContext>()
            .UseSqlite(connection)
            .Options;

        var clientApplicationId = Guid.NewGuid();
        await using var dbContext = new CommerceOpsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.ClientApplications.Add(new ClientApplication
        {
            Id = clientApplicationId,
            Name = "Lumora",
            PublicId = "lumora",
            Secret = "secret",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var lumoraClient = new CapturingLumoraClient();
        var service = new OrderTriageService(
            dbContext,
            lumoraClient,
            new OrderRiskScorer(),
            TimeProvider.System);

        var result = await service.RefreshAsync(clientApplicationId, 240, 100);

        Assert.True(result.IsSuccess);
        Assert.Equal(240, lumoraClient.LookbackMinutes);
        Assert.Equal(100, lumoraClient.Limit);
        Assert.Equal(1, result.CandidatesCount);
        Assert.Equal(1, result.UpsertedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Single(dbContext.OrderTriageSnapshots);
    }

    private sealed class CapturingLumoraClient : ILumoraClient
    {
        public int? LookbackMinutes { get; private set; }
        public int? Limit { get; private set; }

        public Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraOrderTriageCandidatesResponse>> GetTriageCandidatesAsync(
            int? lookbackMinutes = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            LookbackMinutes = lookbackMinutes;
            Limit = limit;

            return Task.FromResult(LumoraClientResult<LumoraOrderTriageCandidatesResponse>.Success(
                new LumoraOrderTriageCandidatesResponse([
                    new LumoraOrderTriageCandidate(
                        "327",
                        "327",
                        "pending",
                        "approved",
                        DateTimeOffset.UtcNow.AddHours(-1),
                        true,
                        899,
                        DateTimeOffset.UtcNow,
                        ["order_paid_but_pending"])
                ])));
        }

        public Task<LumoraClientResult<LumoraPaymentInconsistenciesResponse>> GetPaymentInconsistenciesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraInventoryInconsistenciesResponse>> GetInventoryInconsistenciesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraDatabaseIntegrityResponse>> GetDatabaseIntegrityAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraSlowQueriesResponse>> GetSlowQueriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraFailedJobsResponse>> GetFailedJobsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

