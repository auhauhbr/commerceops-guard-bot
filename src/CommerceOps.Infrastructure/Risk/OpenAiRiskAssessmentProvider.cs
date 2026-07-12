using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CommerceOps.Application.Triage;
using Microsoft.Extensions.Options;

namespace CommerceOps.Infrastructure.Risk;

public sealed class OpenAiRiskAssessmentProvider(
    HttpClient httpClient,
    IOptions<AiRiskOptions> options) : IAiRiskAssessmentProvider
{
    private const string SystemPrompt = """
        Você é um classificador operacional defensivo para e-commerce.
        Classifique risco somente com base nos fatos fornecidos.
        Não invente dados.
        Não execute ações.
        Não envie mensagens.
        Não altere pedido, pagamento ou estoque.
        Retorne somente JSON compatível com o schema.
        Se houver estoque negativo ou pagamento aprovado com pedido pendente, nunca classifique como low.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object ResultSchema = new
    {
        type = "object",
        additionalProperties = false,
        properties = new
        {
            risk_level = new { type = "string", @enum = new[] { "low", "medium", "high" } },
            risk_score = new { type = "integer", minimum = 0, maximum = 100 },
            primary_finding_code = new { type = new[] { "string", "null" } },
            summary = new { type = new[] { "string", "null" } },
            reasoning_summary = new { type = new[] { "string", "null" } },
            recommended_action = new { type = new[] { "string", "null" } },
            customer_message_subject = new { type = new[] { "string", "null" } },
            customer_message_body = new { type = new[] { "string", "null" } },
            confidence = new { type = "number", minimum = 0, maximum = 1 }
        },
        required = new[]
        {
            "risk_level", "risk_score", "primary_finding_code", "summary", "reasoning_summary",
            "recommended_action", "customer_message_subject", "customer_message_body", "confidence"
        }
    };

    private readonly AiRiskOptions _options = options.Value;

    public async Task<string> AssessAsync(
        AiRiskAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI risk provider is unavailable because OPENAI_API_KEY is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("OpenAI risk provider is unavailable because AI_RISK_MODEL is not configured.");
        }

        var body = new
        {
            model = _options.Model,
            instructions = SystemPrompt,
            input = JsonSerializer.Serialize(request, JsonOptions),
            temperature = 0.1,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "ai_risk_assessment_result",
                    strict = true,
                    schema = ResultSchema
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI Responses API returned HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var outputText = ExtractOutputText(document.RootElement);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new JsonException("OpenAI Responses API response did not contain structured output text.");
        }

        return outputText;
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }
}
