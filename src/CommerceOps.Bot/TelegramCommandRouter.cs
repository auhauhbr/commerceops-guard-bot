using System.Text;
using CommerceOps.Application.Cases;

namespace CommerceOps.Bot;

public sealed class TelegramCommandRouter(
    IOperationalCaseQueryService caseQueryService,
    IAdminAuthorizationService authorizationService)
{
    private const int CaseListLimit = 10;

    public async Task<string> RouteAsync(TelegramCommandContext context, CancellationToken cancellationToken)
    {
        if (!authorizationService.IsAuthorized(context.UserId))
        {
            return "Acesso bloqueado.";
        }

        var text = context.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return HelpMessage();
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].Split('@', 2)[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        return command switch
        {
            "/start" => StartMessage(),
            "/help" => HelpMessage(),
            "/casos" => await OpenCasesMessageAsync(cancellationToken),
            "/case" => await CaseDetailsMessageAsync(argument, cancellationToken),
            "/resumo" => await SummaryMessageAsync(cancellationToken),
            _ => "Comando nao reconhecido. Use /help."
        };
    }

    private static string StartMessage()
    {
        return """
            CommerceOps Guard

            Use /casos para listar casos abertos.
            Use /case CASE-00001 para ver detalhes.
            """;
    }

    private static string HelpMessage()
    {
        return """
            Comandos:
            /resumo - resumo operacional
            /casos - casos abertos
            /case CASE-00001 - detalhes do caso
            """;
    }

    private async Task<string> OpenCasesMessageAsync(CancellationToken cancellationToken)
    {
        var cases = await caseQueryService.ListOpenCasesAsync(CaseListLimit, cancellationToken);
        if (cases.Count == 0)
        {
            return """
                Inbox operacional

                Abertos:
                Nenhum caso aberto.
                """;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Inbox operacional");
        builder.AppendLine();
        builder.AppendLine("Abertos:");

        for (var index = 0; index < cases.Count; index++)
        {
            var currentCase = cases[index];
            builder.AppendLine($"{index + 1}. {currentCase.CaseNumber} — {currentCase.Title}");
            builder.AppendLine($"   Risco: {currentCase.RiskLevel} | Status: {currentCase.Status}");
        }

        builder.AppendLine();
        builder.AppendLine($"Use /case {cases[0].CaseNumber} para ver detalhes.");

        return builder.ToString().TrimEnd();
    }

    private async Task<string> CaseDetailsMessageAsync(string caseNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
        {
            return "Informe o caso. Ex: /case CASE-00001";
        }

        var currentCase = await caseQueryService.GetCaseByNumberAsync(caseNumber, cancellationToken);
        if (currentCase is null)
        {
            return $"Caso {caseNumber.Trim()} nao encontrado.";
        }

        return $"""
            {currentCase.CaseNumber} — {currentCase.Title}

            Resumo:
            {currentCase.Summary}

            Risco: {currentCase.RiskLevel}
            Status: {currentCase.Status}

            Entidade:
            {currentCase.EntityType} {currentCase.EntityId}
            """;
    }

    private async Task<string> SummaryMessageAsync(CancellationToken cancellationToken)
    {
        var cases = await caseQueryService.ListOpenCasesAsync(50, cancellationToken);
        if (cases.Count == 0)
        {
            return """
                Resumo operacional

                Casos abertos: 0
                """;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Resumo operacional");
        builder.AppendLine();
        builder.AppendLine($"Casos abertos: {cases.Count}");
        builder.AppendLine();
        builder.AppendLine("Por risco:");

        foreach (var group in cases.GroupBy(currentCase => currentCase.RiskLevel).OrderBy(group => group.Key))
        {
            builder.AppendLine($"{group.Key}: {group.Count()}");
        }

        builder.AppendLine();
        builder.AppendLine("Por status:");

        foreach (var group in cases.GroupBy(currentCase => currentCase.Status).OrderBy(group => group.Key))
        {
            builder.AppendLine($"{group.Key}: {group.Count()}");
        }

        return builder.ToString().TrimEnd();
    }
}
