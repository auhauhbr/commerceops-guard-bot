using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CommerceOps.Application.Lumora;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommerceOps.IntegrationTests;

public sealed class LumoraHealthEndpointTests : IClassFixture<CommerceOpsApiFactory>
{
    private const string Secret = "lumora-response-secret";

    private readonly CommerceOpsApiFactory _factory;

    public LumoraHealthEndpointTests(CommerceOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetLumoraHealthReturnsSafeAvailableSummary()
    {
        using var client = CreateClient(LumoraClientResult<LumoraHealthResponse>.Success(new LumoraHealthResponse(
            "healthy",
            DateTimeOffset.Parse("2026-07-06T12:00:00Z"),
            new LumoraComponentHealth("healthy", null),
            new LumoraComponentHealth("healthy", null))));

        var response = await client.GetAsync("/api/integrations/lumora/health");
        var body = await response.Content.ReadAsStringAsync();
        var payload = await response.Content.ReadFromJsonAsync<LumoraHealthEndpointResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("lumora", payload?.Integration);
        Assert.Equal("available", payload?.Status);
        Assert.Equal("healthy", payload?.LumoraStatus);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLumoraHealthReturnsSafeUnavailableSummary()
    {
        using var client = CreateClient(LumoraClientResult<LumoraHealthResponse>.Failure(
            "timeout",
            $"Tempo esgotado ao consultar a Lumora. {Secret}"));

        var response = await client.GetAsync("/api/integrations/lumora/health");
        var body = await response.Content.ReadAsStringAsync();
        var payload = await response.Content.ReadFromJsonAsync<LumoraHealthEndpointResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("unavailable", payload?.Status);
        Assert.Equal("timeout", payload?.ErrorCode);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(LumoraClientResult<LumoraHealthResponse> result)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILumoraClient>();
                services.AddSingleton<ILumoraClient>(new FakeLumoraClient(result));
            });
        }).CreateClient();
    }

    private sealed class FakeLumoraClient : ILumoraClient
    {
        private readonly LumoraClientResult<LumoraHealthResponse> _healthResult;

        public FakeLumoraClient(LumoraClientResult<LumoraHealthResponse> healthResult)
        {
            _healthResult = healthResult;
        }

        public Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_healthResult);

        public Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed record LumoraHealthEndpointResponse(
        [property: JsonPropertyName("integration")]
        string Integration,
        [property: JsonPropertyName("status")]
        string Status,
        [property: JsonPropertyName("lumora_status")]
        string? LumoraStatus,
        [property: JsonPropertyName("error_code")]
        string? ErrorCode);
}
