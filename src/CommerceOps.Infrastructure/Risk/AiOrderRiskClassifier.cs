using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommerceOps.Application.Triage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommerceOps.Infrastructure.Risk;

public sealed class AiOrderRiskClassifier(
    IAiRiskAssessmentProvider provider,
    DeterministicOrderRiskClassifier fallback,
    AiRiskAssessmentGuardrail guardrail,
    IOptions<AiRiskOptions> options,
    ILogger<AiOrderRiskClassifier> logger) : IOrderRiskClassifier
{
    private static readonly ConcurrentDictionary<string, AiRiskAssessmentResult> Cache = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AiRiskOptions _options = options.Value;

    public async Task<AiRiskAssessmentResult> ClassifyAsync(
        OrderTriageCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !string.Equals(_options.Provider, AiRiskOptions.OpenAiProvider, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_options.Model) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return await fallback.ClassifyAsync(candidate, now, cancellationToken);
        }

        var request = CreateSanitizedRequest(candidate, now);
        var cacheKey = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)));
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 30)));
            var json = await provider.AssessAsync(request, timeout.Token).WaitAsync(timeout.Token);
            var result = JsonSerializer.Deserialize<AiRiskAssessmentResult>(json, JsonOptions);
            if (!guardrail.IsValid(request, result))
            {
                logger.LogWarning("AI risk response failed guardrails; deterministic fallback selected.");
                return (await fallback.ClassifyAsync(candidate, now, cancellationToken)) with { RiskSource = "fallback" };
            }

            var accepted = result! with { RiskSource = "ai" };
            Cache.TryAdd(cacheKey, accepted);
            return accepted;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI risk classification timed out; deterministic fallback selected.");
        }
        catch (JsonException)
        {
            logger.LogWarning("AI risk response was not valid JSON; deterministic fallback selected.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI risk classification failed; deterministic fallback selected.");
        }

        return (await fallback.ClassifyAsync(candidate, now, cancellationToken)) with { RiskSource = "fallback" };
    }

    private static AiRiskAssessmentRequest CreateSanitizedRequest(OrderTriageCandidate candidate, DateTimeOffset now)
    {
        var findings = (candidate.Findings ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        return new AiRiskAssessmentRequest(
            candidate.OrderId[..Math.Min(candidate.OrderId.Length, 100)],
            candidate.OrderStatus[..Math.Min(candidate.OrderStatus.Length, 100)],
            candidate.PaymentStatus is null ? null : candidate.PaymentStatus[..Math.Min(candidate.PaymentStatus.Length, 100)],
            candidate.TotalValue,
            candidate.ItemCount,
            candidate.HasNegativeStock,
            findings,
            new Dictionary<string, bool>
            {
                ["is_stale_30m"] = candidate.UpdatedAt < now.AddMinutes(-30),
                ["is_high_value"] = candidate.TotalValue is >= 500m,
                ["is_payment_approved"] = candidate.PaymentStatus is "approved" or "paid",
                ["is_order_pending"] = candidate.OrderStatus is "pending" or "pending_payment"
            });
    }
}
