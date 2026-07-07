using System.Text;
using System.Text.Json;
using CommerceOps.Application.Actions;
using CommerceOps.Application.Cases;
using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;

namespace CommerceOps.Bot;

public sealed class TelegramCommandRouter(
    IOperationalCaseQueryService caseQueryService,
    IAdminAuthorizationService authorizationService,
    ILumoraClient lumoraClient,
    ICustomerMessageDraftComposer messageDraftComposer,
    IActionRequestService actionRequestService,
    IOrderTriageService orderTriageService,
    TimeProvider timeProvider)
{
    private const int CaseListLimit = 10;
    private const int DiagnosticListLimit = 5;
    private const int ActionListLimit = 10;

    public async Task<string> RouteAsync(TelegramCommandContext context, CancellationToken cancellationToken)
    {
        var response = await RouteMessageAsync(context, cancellationToken);
        return response.Text;
    }

    public async Task<TelegramCommandResponse> RouteMessageAsync(
        TelegramCommandContext context,
        CancellationToken cancellationToken)
    {
        if (!authorizationService.IsAuthorized(context.UserId))
        {
            return TelegramCommandResponse.TextOnly("Acesso bloqueado.");
        }

        var text = context.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return TelegramCommandResponse.TextOnly(HelpMessage());
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts[0].Split('@', 2)[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        return command switch
        {
            "/start" => TelegramCommandResponse.TextOnly(StartMessage()),
            "/help" => TelegramCommandResponse.TextOnly(HelpMessage()),
            "/casos" => TelegramCommandResponse.TextOnly(await OpenCasesMessageAsync(cancellationToken)),
            "/case" => TelegramCommandResponse.TextOnly(await CaseDetailsMessageAsync(argument, cancellationToken)),
            "/pedido" or "/p" => await OrderDiagnosticMessageAsync(argument, cancellationToken),
            "/triagem" or "/tr" => await OrderTriageMessageAsync(cursor: null, cancellationToken),
            "/mensagem-pedido" or "/msg-pedido" or "/msg" or "/mensagem" => await CustomerMessageDraftMessageAsync(argument, cancellationToken),
            "/preparar-mensagem-pedido" or "/prep" or "/preparar" => await PrepareCustomerMessageActionAsync(argument, context.ChatId, cancellationToken),
            "/acoes" => await PendingActionsMessageAsync(cancellationToken),
            "/confirmar-acao" or "/confirmar" => TelegramCommandResponse.TextOnly(await ApproveActionMessageAsync(argument, context.ChatId, cancellationToken)),
            "/cancelar-acao" or "/cancelar" => TelegramCommandResponse.TextOnly(await CancelActionMessageAsync(argument, context.ChatId, cancellationToken)),
            "/resumo" => TelegramCommandResponse.TextOnly(await SummaryMessageAsync(cancellationToken)),
            _ => TelegramCommandResponse.TextOnly("Comando nao reconhecido. Use /help.")
        };
    }

    public string? GetPreliminaryMessage(TelegramCommandContext context)
    {
        if (!authorizationService.IsAuthorized(context.UserId))
        {
            return null;
        }

        var text = context.Text.Trim();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var command = parts[0].Split('@', 2)[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return null;
        }

        return command switch
        {
            "/pedido" or "/p" => $"Consultando pedido #{argument}...",
            "/mensagem-pedido" or "/msg-pedido" or "/msg" or "/mensagem" => "Gerando rascunho...",
            "/preparar-mensagem-pedido" or "/prep" or "/preparar" => "Preparando ação pendente...",
            _ => null
        };
    }

    public async Task<TelegramCommandResponse> RouteCallbackAsync(
        string callbackData,
        long chatId,
        long userId,
        CancellationToken cancellationToken)
    {
        if (!authorizationService.IsAuthorized(userId))
        {
            return TelegramCommandResponse.TextOnly("Acesso bloqueado.");
        }

        var parts = callbackData.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return TelegramCommandResponse.TextOnly("Ação não reconhecida.");
        }

        return (parts[0], parts[1]) switch
        {
            ("order", "findings") => TelegramCommandResponse.TextOnly(await OrderFindingsMessageAsync(parts[2], cancellationToken)),
            ("order", "items") => TelegramCommandResponse.TextOnly(await OrderItemsMessageAsync(parts[2], cancellationToken)),
            ("order", "diagnostic") => await OrderDiagnosticMessageAsync(parts[2], cancellationToken),
            ("order", "draft") => await CustomerMessageDraftMessageAsync(parts[2], cancellationToken),
            ("order", "prepare") => await PrepareCustomerMessageActionAsync(parts[2], chatId, cancellationToken),
            ("triage", "next") => await OrderTriageMessageAsync(ParseCursor(parts[2]), cancellationToken),
            ("draft", "full") => TelegramCommandResponse.TextOnly(await FullCustomerMessageDraftMessageAsync(parts[2], cancellationToken)),
            ("action", "approve") => TelegramCommandResponse.TextOnly(await ApproveActionMessageAsync(parts[2], chatId, cancellationToken)),
            ("action", "cancel") => TelegramCommandResponse.TextOnly(await CancelActionMessageAsync(parts[2], chatId, cancellationToken)),
            ("action", "draft") => TelegramCommandResponse.TextOnly(await ActionDraftMessageAsync(parts[2], cancellationToken)),
            _ => TelegramCommandResponse.TextOnly("Ação não reconhecida.")
        };
    }

    public string? GetCallbackPreliminaryMessage(string callbackData)
    {
        var parts = callbackData.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return null;
        }

        return (parts[0], parts[1]) switch
        {
            ("order", "findings") or ("order", "items") or ("order", "diagnostic") => $"Consultando pedido #{parts[2]}...",
            ("order", "draft") or ("draft", "full") => "Gerando rascunho...",
            ("order", "prepare") => "Preparando ação pendente...",
            ("triage", "next") => "Consultando triagem...",
            _ => null
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
            /triagem - fila operacional priorizada
            /tr - atalho para /triagem
            /pedido {id} - consulta pedido e abre botões
            /p {id} - atalho para /pedido
            /msg {id} - gera rascunho sem enviar
            /prep {id} - prepara ação pendente
            /acoes - lista ações pendentes
            /confirmar {id} - aprova ação pendente
            /cancelar {id} - cancela ação pendente
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

    private async Task<TelegramCommandResponse> OrderDiagnosticMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return TelegramCommandResponse.TextOnly("Informe o ID do pedido. Exemplo: /pedido 1");
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return TelegramCommandResponse.TextOnly("Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.");
        }

        if (result.IsSuccess && result.Data is not null)
        {
            return new TelegramCommandResponse(
                FormatCompactOrderDiagnostic(result.Data),
                CreateOrderKeyboard(result.Data.OrderId));
        }

        return TelegramCommandResponse.TextOnly(GetSafeLumoraFailureMessage(result.Error));
    }

    private async Task<TelegramCommandResponse> CustomerMessageDraftMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return TelegramCommandResponse.TextOnly("Informe o ID do pedido. Exemplo: /mensagem-pedido 1");
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return TelegramCommandResponse.TextOnly("Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.");
        }

        if (!result.IsSuccess || result.Data is null)
        {
            return TelegramCommandResponse.TextOnly(GetSafeLumoraFailureMessage(result.Error));
        }

        var draft = messageDraftComposer.Compose(result.Data);
        return FormatCustomerMessageDraft(draft, GetOrderReference(result.Data), includeFullBody: false);
    }

    private async Task<string> FullCustomerMessageDraftMessageAsync(string orderId, CancellationToken cancellationToken)
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
        return FormatCustomerMessageDraft(draft, GetOrderReference(result.Data), includeFullBody: true).Text;
    }

    private static TelegramCommandResponse FormatCustomerMessageDraft(
        CustomerMessageDraft draft,
        string orderReference,
        bool includeFullBody)
    {
        const int previewLength = 180;
        var isLong = draft.Body.Length > previewLength;
        var body = includeFullBody || !isLong ? draft.Body : Truncate(draft.Body, previewLength);
        var keyboard = isLong && !includeFullBody
            ? new[]
            {
                new[]
                {
                    new TelegramInlineButton("Ver mensagem completa", $"draft:full:{draft.OrderId}")
                }
            }
            : [];

        return new TelegramCommandResponse(
            $"""
            Rascunho — Pedido #{orderReference}

            Canal sugerido: {FormatChannel(draft.Channel)}
            Assunto: {draft.Subject}

            Mensagem:
            {body}

            Status:
            {draft.Warning}
            """,
            keyboard);
    }

    private static string FormatCompactOrderDiagnostic(LumoraOrderDiagnosticResponse diagnostic)
    {
        var orderReference = GetOrderReference(diagnostic);
        return $"""
            Pedido #{orderReference}

            Status do pedido: {FormatValue(diagnostic.Status)}
            Pagamento: {FormatValue(diagnostic.PaymentStatus)}
            Estoque: {FormatValue(diagnostic.StockStatus)}
            Risco: {FormatValue(diagnostic.Risk)}
            Achados: {diagnostic.Findings.Count}
            Total: R$ {FormatValue(diagnostic.Total)}
            Itens: {diagnostic.Items?.Count ?? 0}

            Nenhuma ação foi executada.
            """;
    }

    private static IReadOnlyList<IReadOnlyList<TelegramInlineButton>> CreateOrderKeyboard(string orderId)
    {
        return
        [
            [
                new TelegramInlineButton("Ver achados", $"order:findings:{orderId}"),
                new TelegramInlineButton("Ver itens", $"order:items:{orderId}")
            ],
            [
                new TelegramInlineButton("Gerar mensagem", $"order:draft:{orderId}"),
                new TelegramInlineButton("Preparar ação", $"order:prepare:{orderId}")
            ]
        ];
    }

    private async Task<TelegramCommandResponse> OrderTriageMessageAsync(
        int? cursor,
        CancellationToken cancellationToken)
    {
        const int pageSize = 10;
        var skip = Math.Max(0, cursor ?? 0);
        var snapshots = await orderTriageService.GetTopAsync(pageSize + 1, skip, cancellationToken);
        if (snapshots.Count == 0)
        {
            return TelegramCommandResponse.TextOnly("""
                Triagem operacional — Lumora

                Nenhum pedido com risco médio ou alto no snapshot atual.
                """);
        }

        var visibleSnapshots = snapshots.Take(pageSize).ToList();
        var builder = new StringBuilder();
        builder.AppendLine("Triagem operacional — Lumora");
        builder.AppendLine();

        var keyboard = new List<IReadOnlyList<TelegramInlineButton>>();
        for (var index = 0; index < visibleSnapshots.Count; index++)
        {
            var snapshot = visibleSnapshots[index];
            var orderReference = GetOrderReference(snapshot);
            builder.AppendLine($"{index + 1}. Pedido #{orderReference}");
            builder.AppendLine($"Risco: {FormatRiskLevel(snapshot.RiskLevel)}, score {snapshot.RiskScore}");
            builder.AppendLine($"Problema: {FormatTriageProblem(snapshot)}");
            builder.AppendLine($"Atualizado: {FormatRelativeAge(snapshot.SourceUpdatedAt, timeProvider.GetUtcNow())}");
            builder.AppendLine();

            keyboard.Add(
            [
                new TelegramInlineButton($"Investigar #{orderReference}", $"order:diagnostic:{snapshot.OrderId}"),
                new TelegramInlineButton($"Preparar ação #{orderReference}", $"order:prepare:{snapshot.OrderId}")
            ]);
        }

        if (snapshots.Count > pageSize)
        {
            keyboard.Add(
            [
                new TelegramInlineButton("Próximos", $"triage:next:{skip + pageSize}")
            ]);
        }

        return new TelegramCommandResponse(builder.ToString().TrimEnd(), keyboard);
    }

    private async Task<TelegramCommandResponse> PrepareCustomerMessageActionAsync(
        string orderId,
        long chatId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return TelegramCommandResponse.TextOnly("Informe o ID do pedido. Exemplo: /preparar-mensagem-pedido 1");
        }

        LumoraClientResult<LumoraOrderDiagnosticResponse> result;
        try
        {
            result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
        }
        catch
        {
            return TelegramCommandResponse.TextOnly("Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.");
        }

        if (!result.IsSuccess || result.Data is null)
        {
            return TelegramCommandResponse.TextOnly(GetSafeLumoraFailureMessage(result.Error));
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
            return TelegramCommandResponse.TextOnly("Não consegui criar a ação pendente agora. Tente novamente em alguns instantes.");
        }

        return FormatCreatedAction(actionRequest);
    }

    private async Task<TelegramCommandResponse> PendingActionsMessageAsync(CancellationToken cancellationToken)
    {
        var actions = await actionRequestService.ListPendingAsync(ActionListLimit, cancellationToken);
        if (actions.Count == 0)
        {
            return TelegramCommandResponse.TextOnly("""
                Ações pendentes:

                Nenhuma ação pendente.
                """);
        }

        var builder = new StringBuilder();
        builder.AppendLine("Ações pendentes:");
        var keyboard = new List<IReadOnlyList<TelegramInlineButton>>();

        foreach (var action in actions)
        {
            builder.AppendLine();
            builder.AppendLine($"{action.PublicId} — Pedido #{action.EntityId} — {FormatActionType(action.Type)}");
            builder.AppendLine($"Status: {action.Status}");
            keyboard.Add(
            [
                new TelegramInlineButton("Confirmar", $"action:approve:{action.PublicId}"),
                new TelegramInlineButton("Cancelar", $"action:cancel:{action.PublicId}")
            ]);
        }

        return new TelegramCommandResponse(builder.ToString().TrimEnd(), keyboard);
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

    private static TelegramCommandResponse FormatCreatedAction(ActionRequestDetails actionRequest)
    {
        return new TelegramCommandResponse(
            $"""
            Ação pendente: {actionRequest.PublicId}

            Tipo: {actionRequest.Type}
            Pedido: #{actionRequest.EntityId}
            Status: {actionRequest.Status}

            Nenhuma mensagem foi enviada.
            """,
            CreateActionKeyboard(actionRequest.PublicId));
    }

    private static IReadOnlyList<IReadOnlyList<TelegramInlineButton>> CreateActionKeyboard(string publicId)
    {
        return
        [
            [
                new TelegramInlineButton("Confirmar", $"action:approve:{publicId}"),
                new TelegramInlineButton("Cancelar", $"action:cancel:{publicId}")
            ],
            [
                new TelegramInlineButton("Ver rascunho", $"action:draft:{publicId}")
            ]
        ];
    }

    private async Task<string> OrderFindingsMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        var diagnostic = await GetOrderDiagnosticDataAsync(orderId, cancellationToken);
        if (diagnostic.ResultMessage is not null)
        {
            return diagnostic.ResultMessage;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Achados do pedido #{GetOrderReference(diagnostic.Data!)}");
        builder.AppendLine();
        AppendFindings(builder, diagnostic.Data!.Findings, limit: null);
        return builder.ToString().TrimEnd();
    }

    private async Task<string> OrderItemsMessageAsync(string orderId, CancellationToken cancellationToken)
    {
        var diagnostic = await GetOrderDiagnosticDataAsync(orderId, cancellationToken);
        if (diagnostic.ResultMessage is not null)
        {
            return diagnostic.ResultMessage;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Itens — Pedido #{GetOrderReference(diagnostic.Data!)}");
        builder.AppendLine();
        AppendItems(builder, diagnostic.Data!.Items, limit: null);
        return builder.ToString().TrimEnd();
    }

    private async Task<(LumoraOrderDiagnosticResponse? Data, string? ResultMessage)> GetOrderDiagnosticDataAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await lumoraClient.GetOrderDiagnosticAsync(orderId, cancellationToken);
            return result.IsSuccess && result.Data is not null
                ? (result.Data, null)
                : (null, GetSafeLumoraFailureMessage(result.Error));
        }
        catch
        {
            return (null, "Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.");
        }
    }

    private async Task<string> ActionDraftMessageAsync(string publicId, CancellationToken cancellationToken)
    {
        var action = await actionRequestService.GetByPublicIdAsync(publicId, cancellationToken);
        if (action is null)
        {
            return $"Ação {publicId.Trim()} não encontrada.";
        }

        var payload = ParseActionPayload(action.PayloadJson);
        return $"""
            Rascunho — {action.PublicId}

            Pedido: #{action.EntityId}
            Canal sugerido: {payload.Channel}
            Assunto: {payload.Subject}

            Mensagem:
            {payload.Body}

            Status:
            Rascunho gerado. Nenhuma mensagem foi enviada.
            """;
    }

    private static void AppendFindings(
        StringBuilder builder,
        IReadOnlyList<LumoraDiagnosticFinding> findings,
        int? limit = DiagnosticListLimit)
    {
        builder.AppendLine("Achados:");
        if (findings.Count == 0)
        {
            builder.AppendLine("Nenhum achado operacional retornado.");
            return;
        }

        var visibleFindings = limit.HasValue ? findings.Take(limit.Value) : findings;
        foreach (var (finding, index) in visibleFindings.Select((finding, index) => (finding, index)))
        {
            builder.AppendLine($"{index + 1}. {FormatFindingTitle(finding.Type)}");
            builder.AppendLine(FormatFindingMessage(finding.Type, finding.Message));
            builder.AppendLine($"Severidade: {FormatSeverity(finding.Severity)}");
            builder.AppendLine();
        }

        if (limit.HasValue && findings.Count > limit.Value)
        {
            builder.AppendLine($"... e mais {findings.Count - limit.Value}");
        }
    }

    private static void AppendItems(
        StringBuilder builder,
        IReadOnlyList<LumoraOrderDiagnosticItem>? items,
        int? limit = DiagnosticListLimit)
    {
        builder.AppendLine("Itens:");
        if (items is null || items.Count == 0)
        {
            builder.AppendLine("Nenhum item retornado.");
            return;
        }

        var visibleItems = limit.HasValue ? items.Take(limit.Value) : items;
        foreach (var item in visibleItems)
        {
            builder.AppendLine($"- {FormatValue(item.ProductName)}");
            builder.AppendLine($"  qtd: {FormatValue(item.Quantity?.ToString())}");
            builder.AppendLine($"  total: R$ {FormatValue(item.Total)}");
            builder.AppendLine($"  estoque atual: {FormatValue(item.CurrentStock?.ToString())}");
        }

        if (limit.HasValue && items.Count > limit.Value)
        {
            builder.AppendLine($"... e mais {items.Count - limit.Value}");
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

    private static string GetOrderReference(OrderTriageSnapshotDetails snapshot)
    {
        return string.IsNullOrWhiteSpace(snapshot.OrderNumber)
            ? snapshot.OrderId
            : snapshot.OrderNumber;
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "não informado" : value;
    }

    private static string FormatFindingTitle(string type)
    {
        return type switch
        {
            "pending_order_without_approved_payment" => "Pedido aguardando pagamento",
            "order_paid_but_pending" => "Pagamento aprovado, mas pedido ainda pendente",
            "cancelled_order_with_approved_payment" => "Pedido cancelado com pagamento aprovado",
            "order_without_items" => "Pedido sem itens",
            "product_missing" => "Produto não encontrado",
            "invalid_item_quantity" => "Quantidade inválida no item",
            "order_total_mismatch" => "Total do pedido divergente",
            "negative_stock" => "Estoque negativo",
            "payment_missing" => "Pagamento não encontrado",
            _ => FormatValue(type).Replace('_', ' ')
        };
    }

    private static string FormatFindingMessage(string type, string? originalMessage)
    {
        return type switch
        {
            "pending_order_without_approved_payment" => "O pedido ainda está pendente e não possui pagamento aprovado.",
            "order_paid_but_pending" => "O pedido possui pagamento aprovado, mas ainda não avançou para a próxima etapa operacional.",
            "cancelled_order_with_approved_payment" => "O pedido está cancelado, mas possui pagamento aprovado. Pode exigir revisão operacional.",
            "order_without_items" => "O pedido não possui itens associados.",
            "product_missing" => "Um item do pedido referencia um produto que não foi encontrado.",
            "invalid_item_quantity" => "Um item do pedido possui quantidade inválida.",
            "order_total_mismatch" => "O total calculado dos itens não bate com o total registrado no pedido.",
            "negative_stock" => "Um ou mais produtos relacionados ao pedido estão com estoque negativo.",
            "payment_missing" => "Não foi encontrado registro de pagamento associado ao pedido.",
            _ => FormatValue(originalMessage)
        };
    }

    private static string FormatTriageProblem(OrderTriageSnapshotDetails snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Summary))
        {
            return snapshot.Summary;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastFindingCode))
        {
            return FormatFindingTitle(snapshot.LastFindingCode).ToLowerInvariant();
        }

        return "pedido requer atenção operacional";
    }

    private static string FormatRiskLevel(string riskLevel)
    {
        return riskLevel switch
        {
            "low" => "baixo",
            "medium" => "médio",
            "high" => "alto",
            "critical" => "crítico",
            _ => FormatValue(riskLevel)
        };
    }

    private static string FormatRelativeAge(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        if (updatedAt >= now)
        {
            return "agora";
        }

        var age = now - updatedAt;
        if (age.TotalMinutes < 1)
        {
            return "agora";
        }

        if (age.TotalHours < 1)
        {
            return $"há {(int)age.TotalMinutes} min";
        }

        if (age.TotalDays < 1)
        {
            return $"há {(int)age.TotalHours} h";
        }

        return $"há {(int)age.TotalDays} d";
    }

    private static int? ParseCursor(string value)
    {
        return int.TryParse(value, out var cursor) ? Math.Max(0, cursor) : null;
    }

    private static string FormatSeverity(string severity)
    {
        return severity switch
        {
            "info" => "informativa",
            "low" => "baixa",
            "medium" => "média",
            "high" => "alta",
            "critical" => "crítica",
            _ => FormatValue(severity)
        };
    }

    private static string FormatChannel(NotificationChannel channel)
    {
        return channel switch
        {
            NotificationChannel.Email => "email",
            _ => channel.ToString().ToLowerInvariant()
        };
    }

    private static string FormatActionType(string type)
    {
        return type switch
        {
            ActionRequestTypes.CustomerMessageEmail => "mensagem ao cliente",
            _ => type
        };
    }

    private static ActionPayloadPreview ParseActionPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return new ActionPayloadPreview(
                root.TryGetProperty("channel", out var channel) ? FormatValue(channel.GetString()) : "não informado",
                root.TryGetProperty("subject", out var subject) ? FormatValue(subject.GetString()) : "não informado",
                root.TryGetProperty("body", out var body) ? FormatValue(body.GetString()) : "não informado");
        }
        catch (JsonException)
        {
            return new ActionPayloadPreview("não informado", "não informado", "não informado");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : $"{value[..maxLength].TrimEnd()}...";
    }

    private sealed record ActionPayloadPreview(string Channel, string Subject, string Body);
}
