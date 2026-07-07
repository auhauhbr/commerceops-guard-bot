using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommerceOps.Application.Lumora;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommerceOps.Infrastructure.Lumora;

public sealed class LumoraClient : ILumoraClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptions<LumoraOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LumoraClient> _logger;

    public LumoraClient(
        HttpClient httpClient,
        IOptions<LumoraOptions> options,
        TimeProvider timeProvider,
        ILogger<LumoraClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetAsync<LumoraHealthResponse>("/commerceops/health", cancellationToken);

    public Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Task.FromResult(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure(
                "invalid_order_id",
                "O identificador do pedido e obrigatorio."));
        }

        return GetAsync<LumoraOrderDiagnosticResponse>(
            $"/commerceops/orders/{Uri.EscapeDataString(orderId)}/diagnostic",
            cancellationToken);
    }

    public Task<LumoraClientResult<LumoraPaymentInconsistenciesResponse>> GetPaymentInconsistenciesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<LumoraPaymentInconsistenciesResponse>("/commerceops/payments/inconsistencies", cancellationToken);

    public Task<LumoraClientResult<LumoraInventoryInconsistenciesResponse>> GetInventoryInconsistenciesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<LumoraInventoryInconsistenciesResponse>("/commerceops/inventory/inconsistencies", cancellationToken);

    public Task<LumoraClientResult<LumoraDatabaseIntegrityResponse>> GetDatabaseIntegrityAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<LumoraDatabaseIntegrityResponse>("/commerceops/database/integrity", cancellationToken);

    public Task<LumoraClientResult<LumoraSlowQueriesResponse>> GetSlowQueriesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<LumoraSlowQueriesResponse>("/commerceops/database/slow-queries", cancellationToken);

    public Task<LumoraClientResult<LumoraFailedJobsResponse>> GetFailedJobsAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<LumoraFailedJobsResponse>("/commerceops/jobs/failed", cancellationToken);

    private async Task<LumoraClientResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var validationError = ValidateOptions();
        if (validationError is not null)
        {
            return LumoraClientResult<T>.Failure(validationError.Value.Code, validationError.Value.Message);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        SignRequest(request, path);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout while calling Lumora endpoint {Endpoint}.", path);
            return LumoraClientResult<T>.Failure("timeout", "Tempo esgotado ao consultar a Lumora.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Lumora endpoint {Endpoint} is unavailable.", path);
            return LumoraClientResult<T>.Failure("unavailable", "Nao foi possivel conectar com a Lumora.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return LumoraClientResult<T>.Failure(
                        "not_found",
                        "Recurso nao encontrado na Lumora.",
                        (int)response.StatusCode);
                }

                _logger.LogWarning(
                    "Lumora endpoint {Endpoint} returned non-success status {StatusCode}.",
                    path,
                    (int)response.StatusCode);

                return LumoraClientResult<T>.Failure(
                    "http_error",
                    "A Lumora retornou uma resposta de erro.",
                    (int)response.StatusCode);
            }

            try
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<T>(payload, JsonOptions);
                if (data is null)
                {
                    return LumoraClientResult<T>.Failure("invalid_response", "A Lumora retornou uma resposta vazia ou invalida.");
                }

                return LumoraClientResult<T>.Success(data);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Lumora endpoint {Endpoint} returned invalid JSON.", path);
                return LumoraClientResult<T>.Failure("invalid_response", "A Lumora retornou JSON invalido.");
            }
        }
    }

    private (string Code, string Message)? ValidateOptions()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.AppId) ||
            string.IsNullOrWhiteSpace(options.BaseUrl) ||
            string.IsNullOrWhiteSpace(options.SharedSecret))
        {
            return ("not_configured", "Integracao Lumora nao configurada.");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            return ("not_configured", "URL base da Lumora invalida.");
        }

        return null;
    }

    private void SignRequest(HttpRequestMessage request, string path)
    {
        var options = _options.Value;
        var timestamp = _timeProvider.GetUtcNow().ToString("O");
        var pathAndQuery = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.PathAndQuery
            : path;
        var payload = $"{timestamp}.{request.Method.Method}.{pathAndQuery}";
        var signature = ComputeSignature(options.SharedSecret, payload);

        request.Headers.TryAddWithoutValidation("X-CommerceOps-App", options.AppId);
        request.Headers.TryAddWithoutValidation("X-CommerceOps-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-CommerceOps-Signature", $"sha256={signature}");
    }

    private static string ComputeSignature(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var bytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }
}
