using System.Text.Json;
using System.Text.Json.Serialization;
using CommerceOps.Application.Cases;
using CommerceOps.Application.Lumora;
using CommerceOps.Application.Security;
using CommerceOps.Domain;
using CommerceOps.Infrastructure;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCommerceOpsInfrastructure(builder.Configuration);
builder.Services.Configure<EventIngestionOptions>(builder.Configuration.GetSection(EventIngestionOptions.SectionName));
builder.Services.AddSingleton<HmacSignatureService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/integrations/lumora/health", async (
    ILumoraClient lumoraClient,
    CancellationToken cancellationToken) =>
{
    var result = await lumoraClient.GetHealthAsync(cancellationToken);

    if (!result.IsSuccess || result.Data is null)
    {
        return Results.Ok(new
        {
            integration = "lumora",
            status = "unavailable",
            error_code = result.Error?.Code ?? "unknown_error",
            message = GetSafeLumoraHealthMessage(result.Error?.Code)
        });
    }

    return Results.Ok(new
    {
        integration = "lumora",
        status = "available",
        lumora_status = result.Data.Status,
        checked_at = result.Data.CheckedAt,
        database_status = result.Data.Database?.Status,
        queue_status = result.Data.Queue?.Status
    });
});

app.MapGet("/api/integrations/lumora/orders/{id}/diagnostic", async (
    string id,
    ILumoraClient lumoraClient,
    CancellationToken cancellationToken) =>
{
    var result = await lumoraClient.GetOrderDiagnosticAsync(id, cancellationToken);

    if (result.IsSuccess && result.Data is not null)
    {
        return Results.Ok(result.Data);
    }

    if (result.Error?.Code == "not_found" || result.Error?.StatusCode == StatusCodes.Status404NotFound)
    {
        return Results.NotFound(new
        {
            status = "not_found",
            message = "Pedido nao encontrado na Lumora."
        });
    }

    return Results.Ok(new
    {
        integration = "lumora",
        status = "unavailable",
        error_code = result.Error?.Code ?? "unknown_error",
        message = GetSafeLumoraDiagnosticMessage(result.Error?.Code)
    });
});

static string GetSafeLumoraHealthMessage(string? errorCode) =>
    errorCode switch
    {
        "not_configured" => "Integracao Lumora nao configurada.",
        "timeout" => "Tempo esgotado ao consultar a Lumora.",
        "unavailable" => "Nao foi possivel conectar com a Lumora.",
        "invalid_response" => "A Lumora retornou uma resposta invalida.",
        "http_error" => "A Lumora retornou uma resposta de erro.",
        _ => "Nao foi possivel consultar a Lumora."
    };

static string GetSafeLumoraDiagnosticMessage(string? errorCode) =>
    errorCode switch
    {
        "not_configured" => "Integracao Lumora nao configurada.",
        "timeout" => "Tempo esgotado ao consultar a Lumora.",
        "unavailable" => "Nao foi possivel conectar com a Lumora.",
        "invalid_response" => "A Lumora retornou uma resposta invalida.",
        "http_error" => "A Lumora retornou uma resposta de erro.",
        "invalid_order_id" => "Identificador do pedido invalido.",
        _ => "Nao foi possivel consultar o diagnostico do pedido na Lumora."
    };

app.MapPost("/api/events", async (
    HttpRequest request,
    CommerceOpsDbContext dbContext,
    ICaseService caseService,
    HmacSignatureService signatureService,
    IOptions<EventIngestionOptions> options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CommerceOps.Api.Events");

    if (!request.Headers.TryGetValue("X-CommerceOps-App", out var appHeader) ||
        string.IsNullOrWhiteSpace(appHeader))
    {
        return Results.BadRequest(new ErrorResponse("Header X-CommerceOps-App obrigatorio."));
    }

    if (!request.Headers.TryGetValue("X-CommerceOps-Timestamp", out var timestampHeader) ||
        string.IsNullOrWhiteSpace(timestampHeader))
    {
        return Results.BadRequest(new ErrorResponse("Header X-CommerceOps-Timestamp obrigatorio."));
    }

    if (!request.Headers.TryGetValue("X-CommerceOps-Signature", out var signatureHeader) ||
        string.IsNullOrWhiteSpace(signatureHeader))
    {
        return Results.Unauthorized();
    }

    var timestampValue = timestampHeader.ToString();
    if (!DateTimeOffset.TryParse(timestampValue, out var signedAt))
    {
        return Results.BadRequest(new ErrorResponse("Timestamp invalido."));
    }

    var now = timeProvider.GetUtcNow();
    var tolerance = TimeSpan.FromMinutes(options.Value.ReplayWindowMinutes);
    if (signedAt < now.Subtract(tolerance) || signedAt > now.Add(tolerance))
    {
        logger.LogWarning("Rejected event for app {AppPublicId}: timestamp outside replay window.", appHeader.ToString());
        return Results.Unauthorized();
    }

    var publicId = appHeader.ToString();
    var clientApplication = await dbContext.ClientApplications
        .SingleOrDefaultAsync(application => application.PublicId == publicId, cancellationToken);

    if (clientApplication is null || !clientApplication.IsActive)
    {
        logger.LogWarning("Rejected event for unknown or inactive app {AppPublicId}.", publicId);
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    using var reader = new StreamReader(request.Body);
    var rawBody = await reader.ReadToEndAsync(cancellationToken);

    if (!signatureService.IsValidSignature(
            clientApplication.Secret,
            timestampValue,
            rawBody,
            signatureHeader.ToString()))
    {
        logger.LogWarning("Rejected event for app {AppPublicId}: invalid signature.", publicId);
        return Results.Unauthorized();
    }

    EventRequest? eventRequest;
    try
    {
        eventRequest = JsonSerializer.Deserialize<EventRequest>(rawBody, JsonOptions.Default);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new ErrorResponse("Payload JSON invalido."));
    }

    if (eventRequest is null ||
        string.IsNullOrWhiteSpace(eventRequest.EventType) ||
        string.IsNullOrWhiteSpace(eventRequest.EntityType) ||
        string.IsNullOrWhiteSpace(eventRequest.EntityId) ||
        string.IsNullOrWhiteSpace(eventRequest.Severity))
    {
        return Results.BadRequest(new ErrorResponse("Payload do evento incompleto."));
    }

    if (!string.Equals(eventRequest.AppId, publicId, StringComparison.Ordinal))
    {
        return Results.BadRequest(new ErrorResponse("app_id nao corresponde ao header X-CommerceOps-App."));
    }

    var operationalEvent = new OperationalEvent
    {
        Id = Guid.NewGuid(),
        ClientApplicationId = clientApplication.Id,
        EventType = eventRequest.EventType,
        EntityType = eventRequest.EntityType,
        EntityId = eventRequest.EntityId,
        OccurredAt = eventRequest.OccurredAt,
        Severity = eventRequest.Severity,
        RawBody = rawBody,
        DataJson = eventRequest.Data.ValueKind is JsonValueKind.Undefined ? null : eventRequest.Data.GetRawText(),
        ReceivedAt = now
    };

    dbContext.OperationalEvents.Add(operationalEvent);
    await dbContext.SaveChangesAsync(cancellationToken);
    await caseService.EvaluateOperationalEventAsync(operationalEvent, cancellationToken);

    return Results.Accepted($"/api/events", new EventAcceptedResponse("accepted"));
});

app.MapGet("/api/cases", async (CommerceOpsDbContext dbContext, CancellationToken cancellationToken) =>
{
    var cases = await dbContext.OperationalCases
        .AsNoTracking()
        .OrderByDescending(operationalCase => operationalCase.CaseNumber)
        .Select(operationalCase => new
        {
            id = operationalCase.Id,
            case_number = operationalCase.CaseNumber,
            application_id = operationalCase.ClientApplicationId,
            title = operationalCase.Title,
            summary = operationalCase.Summary,
            status = operationalCase.Status,
            risk_level = operationalCase.RiskLevel,
            risk_score = operationalCase.RiskScore,
            entity_type = operationalCase.EntityType,
            entity_id = operationalCase.EntityId,
            created_at = operationalCase.CreatedAt,
            updated_at = operationalCase.UpdatedAt,
            closed_at = operationalCase.ClosedAt
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(cases);
});

app.MapGet("/api/cases/{id:guid}", async (Guid id, CommerceOpsDbContext dbContext, CancellationToken cancellationToken) =>
{
    var operationalCase = await dbContext.OperationalCases
        .AsNoTracking()
        .Where(currentCase => currentCase.Id == id)
        .Select(currentCase => new
        {
            id = currentCase.Id,
            case_number = currentCase.CaseNumber,
            application_id = currentCase.ClientApplicationId,
            title = currentCase.Title,
            summary = currentCase.Summary,
            status = currentCase.Status,
            risk_level = currentCase.RiskLevel,
            risk_score = currentCase.RiskScore,
            entity_type = currentCase.EntityType,
            entity_id = currentCase.EntityId,
            created_at = currentCase.CreatedAt,
            updated_at = currentCase.UpdatedAt,
            closed_at = currentCase.ClosedAt,
            findings = currentCase.Findings
                .OrderBy(finding => finding.Id)
                .Select(finding => new
                {
                    id = finding.Id,
                    case_id = finding.CaseId,
                    type = finding.Type,
                    severity = finding.Severity,
                    title = finding.Title,
                    description = finding.Description,
                    evidence_json = finding.EvidenceJson,
                    created_at = finding.CreatedAt
                })
                .ToList()
        })
        .SingleOrDefaultAsync(cancellationToken);

    return operationalCase is null ? Results.NotFound() : Results.Ok(operationalCase);
});

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<ClientApplicationSeeder>();
    await seeder.SeedAsync();
}

app.Run();

public partial class Program;

public sealed class EventIngestionOptions
{
    public const string SectionName = "EventIngestion";

    public int ReplayWindowMinutes { get; set; } = 5;
}

internal sealed record EventRequest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("entity_type")] string EntityType,
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("data")] JsonElement Data);

internal sealed record ErrorResponse(string Error);

internal sealed record EventAcceptedResponse(string Status);

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
