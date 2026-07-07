using System.Text;
using System.Text.Json;
using CommerceOps.Application.Actions;
using CommerceOps.Application.Cases;
using CommerceOps.Application.Lumora;

namespace CommerceOps.Bot;

public sealed class TelegramCommandRouter(
    IOperationalCaseQueryService caseQueryService,
    IAdminAuthorizationService authorizationService,
    ILumoraClient lumoraClient,
    ICustomerMessageDraftComposer messageDraftComposer,
    IActionRequestService actionRequestService)
{
    private const int CaseListLimit = 10;
    private const int DiagnosticListLimit = 5;
    private const int ActionListLimit = 10;

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
            "/pedido" => await OrderDiagnosticMessageAsync(argument, cancellationToken),
            "/mensagem-pedido" => await CustomerMessageDraftMessageAsync(argument, cancellationToken),
            "/msg-pedido" => await CustomerMessageDraftMessageAsync(argument, cancellationToken),
            "/preparar-mensagem-pedido" => await PrepareCustomerMessageActionAsync(argument, context.ChatId, cancellationToken),
            "/acoes" => await PendingActionsMessageAsync(cancellationToken),
            "/confirmar-acao" => await ApproveActionMessageAsync(argument, context.ChatId, cancellationToken),
            "/cancelar-acao" => await CancelActionMessageAsync(argument, context.ChatId, cancellationToken),
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
            /pedido {id} - consulta diagnóstico operacional de um pedido da Lumora
            /mensagem-pedido {id} - gera rascunho de mensagem para o cliente, sem enviar
            /preparar-mensagem-pedido {id} - cria ação pendente com rascunho de mensagem ao cliente
            /acoes - lista ações pendentes
            /confirmar-acao {id} - aprova uma ação pendente
            /cancelar-acao {id} - cancela uma ação pendente
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

    private async Task<string> OrderDiagnosticMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return "Informe o ID do pedido. Exemplo: /pedido 1";
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return "Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.";
        }

        if (result.IsSuccess && result.Data is not null)
        {
            return FormatOrderDiagnostic(result.Data);
        }

        return GetSafeLumoraFailureMessage(result.Error);
    }

    private async Task<string> CustomerMessageDraftMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return "Informe o ID do pedido. Exemplo: /mensagem-pedido 1";
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return "Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.";
        }

        if (!result.IsSuccess || result.Data is null)
        {
            return GetSafeLumoraFailureMessage(result.Error);
        }

        var draft = messageDraftComposer.Compose(result.Data);
        var orderReference = GetOrderReference(result.Data);

        return $"""
            Rascunho de mensagem para cliente — Pedido #{orderReference}

            Canal sugerido: {FormatChannel(draft.Channel)}
            Assunto: {draft.Subject}

            Mensagem:
            {draft.Body}

            Status:
            {draft.Warning}
            """;
    }

    private static string FormatOrderDiagnostic(LumoraOrderDiagnosticResponse diagnostic)
    {
        var orderReference = GetOrderReference(diagnostic);
        var builder = new StringBuilder();

        builder.AppendLine($"Pedido #{orderReference} — Diagnóstico Lumora");
        builder.AppendLine();
        builder.AppendLine($"Status do pedido: {FormatValue(diagnostic.Status)}");
        builder.AppendLine($"Pagamento: {FormatValue(diagnostic.PaymentStatus)}");
        builder.AppendLine($"Estoque: {FormatValue(diagnostic.StockStatus)}");
        builder.AppendLine($"Risco: {FormatValue(diagnostic.Risk)}");
        builder.AppendLine();
        AppendFindings(builder, diagnostic.Findings);
        builder.AppendLine();
        AppendItems(builder, diagnostic.Items);
        builder.AppendLine();
        builder.AppendLine("Resumo:");
        builder.AppendLine(FormatValue(diagnostic.Summary));
        builder.AppendLine();
        builder.AppendLine($"Use /mensagem-pedido {diagnostic.OrderId} para gerar um rascunho de mensagem ao cliente.");

        return builder.ToString().TrimEnd();
    }

    private async Task<string> PrepareCustomerMessageActionAsync(
        string orderId,
        long chatId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return "Informe o ID do pedido. Exemplo: /preparar-mensagem-pedido 1";
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return "Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.";
        }

        if (!result.IsSuccess || result.Data is null)
        {
            return GetSafeLumoraFailureMessage(result.Error);
        }

        var draft = messageDraftComposer.Compose(result.Data);
        ActionRequestDetails actionRequest;
        try
        {
            actionRequest = await actionRequestService.CreateCustomerMessageEmailAsync(
                new CreateCustomerMessageActionRequest(
                    draft,
                    result.Data.OrderId,
                    result.Data.OrderNumber,
                    chatId),
                cancellationToken);
        }
        catch
        {
            return "Não consegui criar a ação pendente agora. Tente novamente em alguns instantes.";
        }

        return FormatCreatedAction(actionRequest);
    }

    private async Task<string> PendingActionsMessageAsync(CancellationToken cancellationToken)
    {
        var actions = await actionRequestService.ListPendingAsync(ActionListLimit, cancellationToken);
        if (actions.Count == 0)
        {
            return """
                Ações pendentes:

                Nenhuma ação pendente.
                """;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Ações pendentes:");

        foreach (var action in actions)
        {
            builder.AppendLine();
            builder.AppendLine(action.PublicId);
            builder.AppendLine($"Tipo: {action.Type}");
            builder.AppendLine($"Pedido: #{action.EntityId}");
            builder.AppendLine($"Status: {action.Status}");
            builder.AppendLine($"Criada em: {action.CreatedAt:O}");
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string> ApproveActionMessageAsync(string publicId, long chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return "Informe a ação. Exemplo: /confirmar-acao ACT-00001";
        }

        var action = await actionRequestService.ApproveAsync(publicId, chatId, cancellationToken);
        if (action is null)
        {
            return $"Ação {publicId.Trim()} não encontrada ou não está pendente.";
        }

        return $"""
            Ação {action.PublicId} aprovada.

            Nesta etapa nenhuma mensagem foi enviada.
            A execução real será implementada na próxima fase, quando a Lumora expuser o endpoint seguro de envio.
            """;
    }

    private async Task<string> CancelActionMessageAsync(string publicId, long chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return "Informe a ação. Exemplo: /cancelar-acao ACT-00001";
        }

        var action = await actionRequestService.CancelAsync(publicId, chatId, cancellationToken);
        if (action is null)
        {
            return $"Ação {publicId.Trim()} não encontrada ou não está pendente.";
        }

        return $"Ação {action.PublicId} cancelada.";
    }

    private static string FormatCreatedAction(ActionRequestDetails actionRequest)
    {
        var payload = ParseActionPayload(actionRequest.PayloadJson);
        var preview = Truncate(payload.Body, 260);

        return $"""
            Ação pendente criada: {actionRequest.PublicId}

            Tipo: {actionRequest.Type}
            Pedido: #{actionRequest.EntityId}
            Risco: {FormatValue(actionRequest.Risk)}
            Status: {actionRequest.Status}

            Assunto:
            {payload.Subject}

            Mensagem:
            {preview}

            Nenhuma mensagem foi enviada.

            Para confirmar:
             /confirmar-acao {actionRequest.PublicId}

            Para cancelar:
             /cancelar-acao {actionRequest.PublicId}
            """;
    }

    private static void AppendFindings(StringBuilder builder, IReadOnlyList<LumoraDiagnosticFinding> findings)
    {
        builder.AppendLine("Achados:");
        if (findings.Count == 0)
        {
            builder.AppendLine("Nenhum achado operacional retornado.");
            return;
        }

        foreach (var (finding, index) in findings.Take(DiagnosticListLimit).Select((finding, index) => (finding, index)))
        {
            builder.AppendLine($"{index + 1}. {FormatValue(finding.Type)}");
            builder.AppendLine($"   {FormatValue(finding.Message)}");
        }

        if (findings.Count > DiagnosticListLimit)
        {
            builder.AppendLine($"... e mais {findings.Count - DiagnosticListLimit}");
        }
    }

    private static void AppendItems(StringBuilder builder, IReadOnlyList<LumoraOrderDiagnosticItem>? items)
    {
        builder.AppendLine("Itens:");
        if (items is null || items.Count == 0)
        {
            builder.AppendLine("Nenhum item retornado.");
            return;
        }

        foreach (var item in items.Take(DiagnosticListLimit))
        {
            builder.AppendLine($"- {FormatValue(item.ProductName)}");
            builder.AppendLine($"  qtd: {FormatValue(item.Quantity?.ToString())}");
            builder.AppendLine($"  total: R$ {FormatValue(item.Total)}");
            builder.AppendLine($"  estoque atual: {FormatValue(item.CurrentStock?.ToString())}");
        }

        if (items.Count > DiagnosticListLimit)
        {
            builder.AppendLine($"... e mais {items.Count - DiagnosticListLimit}");
        }
    }

    private static string GetSafeLumoraFailureMessage(LumoraClientError? error)
    {
        if (error?.Code == "not_found" || error?.StatusCode == 404)
        {
            return "Pedido não encontrado na Lumora.";
        }

        return "Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.";
    }

    private static string GetOrderReference(LumoraOrderDiagnosticResponse diagnostic)
    {
        return string.IsNullOrWhiteSpace(diagnostic.OrderNumber)
            ? diagnostic.OrderId
            : diagnostic.OrderNumber;
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "não informado" : value;
    }

    private static string FormatChannel(NotificationChannel channel)
    {
        return channel switch
        {
            NotificationChannel.Email => "email",
            _ => channel.ToString().ToLowerInvariant()
        };
    }

    private static ActionPayloadPreview ParseActionPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return new ActionPayloadPreview(
                root.TryGetProperty("subject", out var subject) ? FormatValue(subject.GetString()) : "não informado",
                root.TryGetProperty("body", out var body) ? FormatValue(body.GetString()) : "não informado");
        }
        catch (JsonException)
        {
            return new ActionPayloadPreview("não informado", "não informado");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : $"{value[..maxLength].TrimEnd()}...";
    }

    private sealed record ActionPayloadPreview(string Subject, string Body);
}
