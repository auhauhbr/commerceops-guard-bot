using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;
using CommerceOps.Domain;
using CommerceOps.Infrastructure.Persistence;
using CommerceOps.Infrastructure.Lumora;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace CommerceOps.IntegrationTests;

public sealed class OrderTriageServiceTests
{
    [Fact]
    public async Task RefreshPersistsAcceptedAiClassificationInSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CommerceOpsDbContext>().UseSqlite(connection).Options;
        var clientApplicationId = Guid.NewGuid();
        await using var dbContext = new CommerceOpsDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.ClientApplications.Add(new ClientApplication
        {
            Id = clientApplicationId, Name = "Lumora", PublicId = "lumora", Secret = "secret", IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var classifier = new FixedRiskClassifier(new AiRiskAssessmentResult(
            "high", 91, "order_paid_but_pending", "Resumo gerado pela IA", "motivo", "revisar",
            "assunto", "corpo", 0.92, "ai"));
        var service = new OrderTriageService(dbContext, new CapturingLumoraClient(), classifier, TimeProvider.System);

        await service.RefreshAsync(clientApplicationId);

        var snapshot = await dbContext.OrderTriageSnapshots.SingleAsync();
        Assert.Equal(91, snapshot.RiskScore);
        Assert.Equal("high", snapshot.RiskLevel);
        Assert.Equal("Resumo gerado pela IA", snapshot.Summary);
        Assert.Equal(1, classifier.Calls);
    }

    [Fact]
    public async Task RefreshAppliesLumoraFindingsFromJsonToSnapshots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<CommerceOpsDbContext>()
            .UseSqlite(connection)
            .Options;

        var clientApplicationId = Guid.NewGuid();
        await using var dbContext = new CommerceOpsDbContext(dbOptions);
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

        var handler = new JsonResponseHandler("""
        {
          "items": [
            {
              "order_id": "1", "order_number": "1", "order_status": "pending",
              "payment_status": "pending", "payment_approved_at": null,
              "has_negative_stock": false, "total_value": 100,
              "updated_at": "2026-07-11T11:55:00Z",
              "findings": ["order_paid_but_pending"]
            },
            {
              "order_id": "2", "order_number": "2", "order_status": "pending",
              "payment_status": null, "payment_approved_at": null,
              "has_negative_stock": false, "total_value": 100,
              "updated_at": "2026-07-11T11:55:00Z",
              "findings": ["pending_order_without_approved_payment", "payment_missing"]
            },
            {
              "order_id": "3", "order_number": "3", "order_status": "pending",
              "payment_status": "pending", "payment_approved_at": null,
              "has_negative_stock": false, "total_value": 100,
              "updated_at": "2026-07-11T11:55:00Z",
              "findings": ["negative_stock"]
            },
            {
              "order_id": "4", "order_number": "4", "order_status": "pending",
              "payment_status": "pending", "payment_approved_at": null,
              "has_negative_stock": false, "total_value": 100,
              "updated_at": "2026-07-11T11:55:00Z",
              "findings": ["order_total_mismatch"]
            }
          ]
        }
        """);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://lumora.test") };
        var timeProvider = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-11T12:00:00Z"));
        var lumoraClient = new LumoraClient(
            httpClient,
            Options.Create(new LumoraOptions
            {
                BaseUrl = "https://lumora.test",
                AppId = "lumora",
                SharedSecret = "secret"
            }),
            timeProvider,
            NullLogger<LumoraClient>.Instance);
        var service = new OrderTriageService(
            dbContext,
            lumoraClient,
            new DeterministicOrderRiskClassifier(new OrderRiskScorer()),
            timeProvider);

        var result = await service.RefreshAsync(clientApplicationId);

        Assert.True(result.IsSuccess);
        var snapshots = await dbContext.OrderTriageSnapshots
            .AsNoTracking()
            .OrderBy(snapshot => snapshot.OrderId)
            .ToListAsync();

        Assert.Collection(
            snapshots,
            snapshot => AssertSnapshot(snapshot, 70, "high", "order_paid_but_pending"),
            snapshot => AssertSnapshot(snapshot, 40, "medium", "pending_order_without_approved_payment"),
            snapshot => AssertSnapshot(snapshot, 70, "high", "negative_stock"),
            snapshot => AssertSnapshot(snapshot, 50, "medium", "order_total_mismatch"));
    }

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
            new DeterministicOrderRiskClassifier(new OrderRiskScorer()),
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

    private sealed class FixedRiskClassifier(AiRiskAssessmentResult result) : IOrderRiskClassifier
    {
        public int Calls { get; private set; }

        public Task<AiRiskAssessmentResult> ClassifyAsync(
            OrderTriageCandidate candidate,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static void AssertSnapshot(
        OrderTriageSnapshot snapshot,
        int minimumScore,
        string level,
        string findingCode)
    {
        Assert.True(snapshot.RiskScore >= minimumScore);
        Assert.InRange(snapshot.RiskScore, 0, 100);
        Assert.Equal(level, snapshot.RiskLevel);
        Assert.Equal(findingCode, snapshot.LastFindingCode);
        Assert.NotEqual("stale_order", snapshot.LastFindingCode);
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
