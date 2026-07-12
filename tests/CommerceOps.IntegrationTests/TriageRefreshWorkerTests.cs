using CommerceOps.Application.Triage;
using CommerceOps.Domain;
using CommerceOps.Infrastructure.Persistence;
using CommerceOps.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommerceOps.IntegrationTests;

public sealed class TriageRefreshWorkerTests
{
    [Fact]
    public async Task WorkerDoesNotRunWhenDisabled()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var worker = new TriageRefreshWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TriageRefreshOptions { Enabled = false }),
            NullLogger<TriageRefreshWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkerRefreshesWhenEnabled()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = new CapturingTriageService();
        var provider = fixture.CreateServiceProvider(service);
        var worker = CreateWorker(provider);

        await worker.StartAsync(CancellationToken.None);
        await service.WaitForCallsAsync(1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(fixture.ClientApplicationId, service.ClientApplicationId);
        Assert.Equal(240, service.LookbackMinutes);
        Assert.Equal(100, service.Limit);
    }

    [Fact]
    public async Task RefreshFailureDoesNotStopWorker()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = new CapturingTriageService { ThrowOnFirstCall = true };
        var provider = fixture.CreateServiceProvider(service);
        var worker = CreateWorker(provider, intervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);
        await service.WaitForCallsAsync(2);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(service.Calls >= 2);
    }

    private static TriageRefreshWorker CreateWorker(ServiceProvider provider, int intervalSeconds = 300)
    {
        return new TriageRefreshWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TriageRefreshOptions
            {
                Enabled = true,
                IntervalSeconds = intervalSeconds,
                LookbackMinutes = 240,
                Limit = 100,
                ClientApplicationPublicId = "lumora"
            }),
            NullLogger<TriageRefreshWorker>.Instance);
    }

    private static async Task<SqliteFixture> CreateFixtureAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
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

        return new SqliteFixture(connection, clientApplicationId);
    }

    private sealed class SqliteFixture(
        SqliteConnection connection,
        Guid clientApplicationId) : IAsyncDisposable
    {
        public Guid ClientApplicationId { get; } = clientApplicationId;

        public ServiceProvider CreateServiceProvider(IOrderTriageService triageService)
        {
            return new ServiceCollection()
                .AddDbContext<CommerceOpsDbContext>(options => options.UseSqlite(connection))
                .AddScoped(_ => triageService)
                .BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
        }
    }

    private sealed class CapturingTriageService : IOrderTriageService
    {
        private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowOnFirstCall { get; init; }
        public int Calls { get; private set; }
        public Guid ClientApplicationId { get; private set; }
        public int? LookbackMinutes { get; private set; }
        public int? Limit { get; private set; }

        public Task<OrderTriageRefreshResult> RefreshAsync(
            Guid clientApplicationId,
            int? lookbackMinutes = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ClientApplicationId = clientApplicationId;
            LookbackMinutes = lookbackMinutes;
            Limit = limit;

            if (Calls == 1)
            {
                _firstCall.TrySetResult();
                if (ThrowOnFirstCall)
                {
                    throw new InvalidOperationException("transient failure");
                }
            }

            if (Calls >= 2)
            {
                _secondCall.TrySetResult();
            }

            return Task.FromResult(new OrderTriageRefreshResult(1, 1, 0));
        }

        public Task<IReadOnlyList<OrderTriageSnapshotDetails>> GetTopAsync(
            int limit,
            int? cursor = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OrderTriageSnapshotDetails>>([]);
        }

        public async Task WaitForCallsAsync(int expectedCalls)
        {
            var waitTask = expectedCalls <= 1 ? _firstCall.Task : _secondCall.Task;
            var completedTask = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(waitTask, completedTask);
        }
    }
}
