using CommerceOps.Application.Actions;
using CommerceOps.Application.Cases;
using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;
using CommerceOps.Bot;

namespace CommerceOps.UnitTests;

public sealed class TelegramCommandRouterTests
{
    [Fact]
    public async Task RouteAsyncBlocksUnauthorizedUser()
    {
        var router = CreateRouter(isAuthorized: false);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 999, "/casos"), CancellationToken.None);

        Assert.Equal("Acesso bloqueado.", response);
    }

    [Fact]
    public async Task RouteAsyncListsOpenCases()
    {
        var queryService = new FakeCaseQueryService
        {
            OpenCases =
            [
                new CaseSummary("CASE-00001", "Pedido pago não confirmado", "medium", "open")
            ]
        };
        var router = CreateRouter(queryService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/casos"), CancellationToken.None);

        Assert.Contains("Inbox operacional", response);
        Assert.Contains("1. CASE-00001 — Pedido pago não confirmado", response);
        Assert.Contains("Risco: medium | Status: open", response);
        Assert.Contains("Use /case CASE-00001 para ver detalhes.", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsCaseDetails()
    {
        var queryService = new FakeCaseQueryService
        {
            CaseDetails = new CaseDetails(
                "CASE-00001",
                "Pedido pago não confirmado",
                "Pagamento aprovado para order 1042, mas o pedido permanece pendente.",
                "medium",
                "open",
                "order",
                "1042")
        };
        var router = CreateRouter(queryService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/case CASE-00001"), CancellationToken.None);

        Assert.Contains("CASE-00001 — Pedido pago não confirmado", response);
        Assert.Contains("Resumo:", response);
        Assert.Contains("Pagamento aprovado para order 1042, mas o pedido permanece pendente.", response);
        Assert.Contains("Risco: medium", response);
        Assert.Contains("Status: open", response);
        Assert.Contains("order 1042", response);
    }

    [Fact]
    public async Task RouteAsyncSummarizesOpenCasesByRiskAndStatus()
    {
        var queryService = new FakeCaseQueryService
        {
            OpenCases =
            [
                new CaseSummary("CASE-00002", "Estoque negativo", "medium", "open"),
                new CaseSummary("CASE-00001", "Pedido pago não confirmado", "medium", "open")
            ]
        };
        var router = CreateRouter(queryService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/resumo"), CancellationToken.None);

        Assert.Contains("Casos abertos: 2", response);
        Assert.Contains("medium: 2", response);
        Assert.Contains("open: 2", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsHelpForStart()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/start"), CancellationToken.None);

        Assert.Contains("CommerceOps Guard", response);
        Assert.Contains("/casos", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsUsageWhenPedidoHasNoId()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/pedido"), CancellationToken.None);

        Assert.Equal("Informe o ID do pedido. Exemplo: /pedido 1", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsFormattedOrderDiagnostic()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/pedido 1"), CancellationToken.None);

        Assert.Contains("Pedido #1", response);
        Assert.Contains("Status do pedido: pending_payment", response);
        Assert.Contains("Pagamento: pending", response);
        Assert.Contains("Estoque: ok", response);
        Assert.Contains("Risco: low", response);
        Assert.Contains("Achados: 1", response);
        Assert.Contains("Total: R$ 899.00", response);
        Assert.Contains("Itens: 1", response);
        Assert.Contains("Nenhuma ação foi executada.", response);
        Assert.DoesNotContain("Order is pending and does not have an approved payment.", response);
    }

    [Fact]
    public async Task RouteMessageAsyncAddsInlineButtonsForOrderDiagnostic()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())));

        var response = await router.RouteMessageAsync(new TelegramCommandContext(1, 123, "/p 1"), CancellationToken.None);

        Assert.Contains("Pedido #1", response.Text);
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Ver achados" && button.CallbackData == "order:findings:1");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Ver itens" && button.CallbackData == "order:items:1");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Gerar mensagem" && button.CallbackData == "order:draft:1");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Preparar ação" && button.CallbackData == "order:prepare:1");
    }

    [Fact]
    public async Task RouteMessageAsyncListsOperationalTriage()
    {
        var triageService = new FakeOrderTriageService
        {
            Snapshots =
            [
                CreateTriageSnapshot(
                    orderId: "327",
                    score: 82,
                    level: "high",
                    summary: "pagamento aprovado, mas pedido ainda pendente",
                    sourceUpdatedAt: DateTimeOffset.Parse("2026-07-07T11:48:00Z"))
            ]
        };
        var router = CreateRouter(orderTriageService: triageService);

        var response = await router.RouteMessageAsync(new TelegramCommandContext(1, 123, "/triagem"), CancellationToken.None);

        Assert.Contains("Triagem operacional — Lumora", response.Text);
        Assert.Contains("1. Pedido #327", response.Text);
        Assert.Contains("Risco: alto, score 82", response.Text);
        Assert.Contains("Problema: pagamento aprovado, mas pedido ainda pendente", response.Text);
        Assert.Contains("Atualizado: há 12 min", response.Text);
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Investigar #327" && button.CallbackData == "order:diagnostic:327");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Preparar ação #327" && button.CallbackData == "order:prepare:327");
    }

    [Fact]
    public async Task RouteAsyncReturnsEmptyOperationalTriage()
    {
        var router = CreateRouter(orderTriageService: new FakeOrderTriageService());

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/tr"), CancellationToken.None);

        Assert.Contains("Triagem operacional — Lumora", response);
        Assert.Contains("Nenhum pedido com risco médio ou alto", response);
    }

    [Fact]
    public async Task RouteCallbackAsyncShowsFullOrderFindings()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())));

        var response = await router.RouteCallbackAsync("order:findings:1", 1, 123, CancellationToken.None);

        Assert.Contains("Achados do pedido #1", response.Text);
        Assert.Contains("1. Pedido aguardando pagamento", response.Text);
        Assert.Contains("O pedido ainda está pendente e não possui pagamento aprovado.", response.Text);
        Assert.Contains("Severidade: informativa", response.Text);
        Assert.DoesNotContain("pending_order_without_approved_payment", response.Text);
        Assert.DoesNotContain("Order is pending and does not have an approved payment.", response.Text);
    }

    [Fact]
    public async Task RouteCallbackAsyncShowsUnknownFindingWithSafeFallback()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic(
                findingType: "new_internal_check_failed",
                findingSeverity: "experimental",
                findingMessage: "New internal check failed."))));

        var response = await router.RouteCallbackAsync("order:findings:1", 1, 123, CancellationToken.None);

        Assert.Contains("1. new internal check failed", response.Text);
        Assert.Contains("New internal check failed.", response.Text);
        Assert.Contains("Severidade: experimental", response.Text);
    }

    [Fact]
    public async Task RouteCallbackAsyncShowsOrderItems()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())));

        var response = await router.RouteCallbackAsync("order:items:1", 1, 123, CancellationToken.None);

        Assert.Contains("Itens — Pedido #1", response.Text);
        Assert.Contains("Ponto De Acesso Ubiquiti UniFi U6+ Wi-Fi 6 Interno", response.Text);
        Assert.Contains("estoque atual: 8", response.Text);
    }

    [Fact]
    public async Task RouteAsyncReturnsSafeNotFoundForMissingOrder()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure("not_found", "secret stack trace", 404)));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/pedido 999"), CancellationToken.None);

        Assert.Equal("Pedido não encontrado na Lumora.", response);
        Assert.DoesNotContain("secret", response);
        Assert.DoesNotContain("stack", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RouteAsyncReturnsSafeUnavailableWhenLumoraFails()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure("unavailable", "private base url")));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/pedido 1"), CancellationToken.None);

        Assert.Equal("Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.", response);
        Assert.DoesNotContain("private", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsUsageWhenMensagemPedidoHasNoId()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/mensagem-pedido"), CancellationToken.None);

        Assert.Equal("Informe o ID do pedido. Exemplo: /mensagem-pedido 1", response);
    }

    [Fact]
    public async Task RouteAsyncGeneratesCustomerMessageDraftWithoutSending()
    {
        var lumoraClient = new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic()));
        var router = CreateRouter(lumoraClient: lumoraClient);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/msg-pedido 1"), CancellationToken.None);

        Assert.Contains("Rascunho — Pedido #1", response);
        Assert.Contains("Canal sugerido: email", response);
        Assert.Contains("Assunto: Atualização sobre seu pedido #1", response);
        Assert.Contains("Mensagem:", response);
        Assert.Contains("Status:", response);
        Assert.Contains("Rascunho gerado. Nenhuma mensagem foi enviada.", response);
        Assert.Equal(1, lumoraClient.OrderDiagnosticCalls);
    }

    [Fact]
    public async Task RouteAsyncSupportsShortMessageAliases()
    {
        var lumoraClient = new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic()));
        var router = CreateRouter(lumoraClient: lumoraClient);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/msg 1"), CancellationToken.None);

        Assert.Contains("Rascunho — Pedido #1", response);
        Assert.Equal(1, lumoraClient.OrderDiagnosticCalls);
    }

    [Fact]
    public async Task RouteAsyncUsesPendingPaymentCustomerMessageTemplate()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic(
                findingType: "pending_order_without_approved_payment"))));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/mensagem-pedido 1"), CancellationToken.None);

        Assert.Contains("Assunto: Atualização sobre seu pedido #1", response);
        Assert.Contains("ainda está aguardando confirmação de pagamento", response);
    }

    [Fact]
    public async Task RouteAsyncUsesPaidButPendingCustomerMessageTemplate()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic(
                findingType: "order_paid_but_pending"))));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/mensagem-pedido 1"), CancellationToken.None);

        Assert.Contains("Assunto: Estamos revisando seu pedido #1", response);
        Assert.Contains("pagamento do seu pedido #1 foi aprovado", response);
        Assert.DoesNotContain("Você não precisa refazer o pedido", response);
    }

    [Fact]
    public async Task RouteMessageAsyncAddsFullMessageButtonForLongDraft()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic(
                findingType: "order_paid_but_pending"))));

        var response = await router.RouteMessageAsync(new TelegramCommandContext(1, 123, "/mensagem 1"), CancellationToken.None);

        Assert.Contains("Rascunho — Pedido #1", response.Text);
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Ver mensagem completa" && button.CallbackData == "draft:full:1");
    }

    [Fact]
    public async Task RouteCallbackAsyncShowsFullDraft()
    {
        var router = CreateRouter(lumoraClient: new FakeLumoraClient(
            LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic(
                findingType: "order_paid_but_pending"))));

        var response = await router.RouteCallbackAsync("draft:full:1", 1, 123, CancellationToken.None);

        Assert.Contains("Você não precisa refazer o pedido", response.Text);
    }

    [Fact]
    public async Task RouteAsyncHelpIncludesNewOrderCommands()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/help"), CancellationToken.None);

        Assert.Contains("/pedido {id} - consulta pedido e abre botões", response);
        Assert.Contains("/msg {id} - gera rascunho sem enviar", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsUsageWhenPrepareMessageHasNoId()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/preparar-mensagem-pedido"), CancellationToken.None);

        Assert.Equal("Informe o ID do pedido. Exemplo: /preparar-mensagem-pedido 1", response);
    }

    [Fact]
    public async Task RouteAsyncCreatesPendingActionRequestForPreparedMessage()
    {
        var actionService = new FakeActionRequestService();
        var router = CreateRouter(
            lumoraClient: new FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())),
            actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(77, 123, "/preparar-mensagem-pedido 1"), CancellationToken.None);

        Assert.Contains("Ação pendente: ACT-00001", response);
        Assert.Contains("Tipo: customer_message_email", response);
        Assert.Contains("Pedido: #1", response);
        Assert.Contains("Status: pending_approval", response);
        Assert.Contains("Nenhuma mensagem foi enviada.", response);
        Assert.Single(actionService.CreatedRequests);
        Assert.Equal(77, actionService.CreatedRequests[0].CreatedByChatId);
    }

    [Fact]
    public async Task RouteMessageAsyncAddsActionButtonsForPreparedMessage()
    {
        var actionService = new FakeActionRequestService();
        var router = CreateRouter(
            lumoraClient: new FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())),
            actionRequestService: actionService);

        var response = await router.RouteMessageAsync(new TelegramCommandContext(77, 123, "/prep 1"), CancellationToken.None);

        Assert.Contains("Ação pendente: ACT-00001", response.Text);
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Confirmar" && button.CallbackData == "action:approve:ACT-00001");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Cancelar" && button.CallbackData == "action:cancel:ACT-00001");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Ver rascunho" && button.CallbackData == "action:draft:ACT-00001");
    }

    [Fact]
    public async Task RouteCallbackAsyncPreparesPendingActionFromOrderButton()
    {
        var actionService = new FakeActionRequestService();
        var router = CreateRouter(
            lumoraClient: new FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Success(CreateDiagnostic())),
            actionRequestService: actionService);

        var response = await router.RouteCallbackAsync("order:prepare:1", 77, 123, CancellationToken.None);

        Assert.Contains("Ação pendente: ACT-00001", response.Text);
        Assert.Single(actionService.CreatedRequests);
        Assert.Equal(77, actionService.CreatedRequests[0].CreatedByChatId);
    }

    [Fact]
    public async Task RouteAsyncDoesNotCreateActionWhenPreparedMessageOrderIsMissing()
    {
        var actionService = new FakeActionRequestService();
        var router = CreateRouter(
            lumoraClient: new FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure("not_found", "missing", 404)),
            actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/preparar-mensagem-pedido 999"), CancellationToken.None);

        Assert.Equal("Pedido não encontrado na Lumora.", response);
        Assert.Empty(actionService.CreatedRequests);
    }

    [Fact]
    public async Task RouteAsyncDoesNotCreateActionWhenLumoraIsUnavailable()
    {
        var actionService = new FakeActionRequestService();
        var router = CreateRouter(
            lumoraClient: new FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure("unavailable", "private url")),
            actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/preparar-mensagem-pedido 1"), CancellationToken.None);

        Assert.Equal("Não consegui consultar a Lumora agora. Tente novamente em alguns instantes.", response);
        Assert.Empty(actionService.CreatedRequests);
    }

    [Fact]
    public async Task RouteAsyncListsPendingActions()
    {
        var actionService = new FakeActionRequestService
        {
            PendingActions =
            [
                CreateActionDetails("ACT-00001", status: ActionRequestStatuses.PendingApproval, entityId: "1")
            ]
        };
        var router = CreateRouter(actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/acoes"), CancellationToken.None);

        Assert.Contains("Ações pendentes:", response);
        Assert.Contains("ACT-00001 — Pedido #1 — mensagem ao cliente", response);
        Assert.Contains("Status: pending_approval", response);
    }

    [Fact]
    public async Task RouteMessageAsyncAddsButtonsForPendingActions()
    {
        var actionService = new FakeActionRequestService
        {
            PendingActions =
            [
                CreateActionDetails("ACT-00001", status: ActionRequestStatuses.PendingApproval, entityId: "1")
            ]
        };
        var router = CreateRouter(actionRequestService: actionService);

        var response = await router.RouteMessageAsync(new TelegramCommandContext(1, 123, "/acoes"), CancellationToken.None);

        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Confirmar" && button.CallbackData == "action:approve:ACT-00001");
        Assert.Contains(response.InlineKeyboard.SelectMany(row => row), button => button.Text == "Cancelar" && button.CallbackData == "action:cancel:ACT-00001");
    }

    [Fact]
    public async Task RouteAsyncApprovesPendingActionWithoutSendingEmail()
    {
        var actionService = new FakeActionRequestService
        {
            ApproveResult = CreateActionDetails("ACT-00001", status: ActionRequestStatuses.Approved)
        };
        var router = CreateRouter(actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(456, 123, "/confirmar-acao ACT-00001"), CancellationToken.None);

        Assert.Contains("Ação ACT-00001 aprovada.", response);
        Assert.Contains("Nesta etapa nenhuma mensagem foi enviada.", response);
        Assert.Contains("endpoint seguro de envio", response);
        Assert.Equal("ACT-00001", actionService.ApprovedPublicId);
        Assert.Equal(456, actionService.ApprovedByChatId);
        Assert.False(actionService.EmailWasSent);
    }

    [Fact]
    public async Task RouteAsyncCancelsPendingAction()
    {
        var actionService = new FakeActionRequestService
        {
            CancelResult = CreateActionDetails("ACT-00001", status: ActionRequestStatuses.Cancelled)
        };
        var router = CreateRouter(actionRequestService: actionService);

        var response = await router.RouteAsync(new TelegramCommandContext(456, 123, "/cancelar-acao ACT-00001"), CancellationToken.None);

        Assert.Equal("Ação ACT-00001 cancelada.", response);
        Assert.Equal("ACT-00001", actionService.CancelledPublicId);
        Assert.Equal(456, actionService.CancelledByChatId);
    }

    [Fact]
    public async Task RouteAsyncHelpIncludesActionCommands()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/help"), CancellationToken.None);

        Assert.Contains("/prep {id} - prepara ação pendente", response);
        Assert.Contains("/acoes - lista ações pendentes", response);
        Assert.Contains("/confirmar {id} - aprova ação pendente", response);
        Assert.Contains("/cancelar {id} - cancela ação pendente", response);
    }

    private static TelegramCommandRouter CreateRouter(
        IOperationalCaseQueryService? queryService = null,
        bool isAuthorized = true,
        ILumoraClient? lumoraClient = null,
        IActionRequestService? actionRequestService = null,
        IOrderTriageService? orderTriageService = null)
    {
        return new TelegramCommandRouter(
            queryService ?? new FakeCaseQueryService(),
            new FakeAuthorizationService(isAuthorized),
            lumoraClient ?? new FakeLumoraClient(),
            new CustomerMessageDraftService(),
            actionRequestService ?? new FakeActionRequestService(),
            orderTriageService ?? new FakeOrderTriageService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-07T12:00:00Z")));
    }

    private static LumoraOrderDiagnosticResponse CreateDiagnostic(
        string findingType = "pending_order_without_approved_payment",
        string findingSeverity = "info",
        string findingMessage = "Order is pending and does not have an approved payment.")
    {
        return new LumoraOrderDiagnosticResponse(
            "1",
            "pending_payment",
            "pending",
            "ok",
            [
                new LumoraDiagnosticFinding(
                    findingType,
                    findingSeverity,
                    findingMessage,
                    null)
            ],
            "1 operational finding(s) detected.",
            "low",
            "1",
            "899.00",
            "899.00",
            "0.00",
            DateTimeOffset.Parse("2026-07-07T01:55:47Z"),
            DateTimeOffset.Parse("2026-07-07T01:55:48Z"),
            [
                new LumoraOrderDiagnosticItem(
                    "1",
                    "1",
                    "Ponto De Acesso Ubiquiti UniFi U6+ Wi-Fi 6 Interno",
                    "899.00",
                    1,
                    "899.00",
                    true,
                    8)
            ]);
    }

    private static ActionRequestDetails CreateActionDetails(
        string publicId,
        string status,
        string entityId = "1")
    {
        return new ActionRequestDetails(
            Guid.NewGuid(),
            publicId,
            ActionRequestTypes.CustomerMessageEmail,
            status,
            "order",
            entityId,
            "low",
            "pending_order_without_approved_payment",
            """
            {
              "channel": "email",
              "subject": "Atualização sobre seu pedido #1",
              "body": "Olá! Identificamos que seu pedido #1 foi criado com sucesso e ainda está aguardando confirmação de pagamento.",
              "order_id": "1",
              "order_number": "1",
              "findings": ["pending_order_without_approved_payment"],
              "warning": "draft_only"
            }
            """,
            123,
            status == ActionRequestStatuses.Approved ? 456 : null,
            status == ActionRequestStatuses.Cancelled ? 456 : null,
            DateTimeOffset.Parse("2026-07-07T03:00:00Z"),
            status == ActionRequestStatuses.Approved ? DateTimeOffset.Parse("2026-07-07T03:05:00Z") : null,
            status == ActionRequestStatuses.Cancelled ? DateTimeOffset.Parse("2026-07-07T03:05:00Z") : null,
            null,
            null);
    }

    private static OrderTriageSnapshotDetails CreateTriageSnapshot(
        string orderId,
        int score,
        string level,
        string? summary,
        DateTimeOffset sourceUpdatedAt)
    {
        return new OrderTriageSnapshotDetails(
            Guid.NewGuid(),
            Guid.NewGuid(),
            orderId,
            orderId,
            score,
            level,
            "order_paid_but_pending",
            summary,
            "pending",
            "approved",
            899m,
            sourceUpdatedAt,
            DateTimeOffset.Parse("2026-07-07T12:00:00Z"),
            false,
            null);
    }

    private sealed class FakeAuthorizationService(bool isAuthorized) : IAdminAuthorizationService
    {
        public bool IsAuthorized(long telegramUserId)
        {
            return isAuthorized;
        }
    }

    private sealed class FakeCaseQueryService : IOperationalCaseQueryService
    {
        public IReadOnlyList<CaseSummary> OpenCases { get; init; } = [];

        public CaseDetails? CaseDetails { get; init; }

        public Task<IReadOnlyList<CaseSummary>> ListOpenCasesAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenCases);
        }

        public Task<CaseDetails?> GetCaseByNumberAsync(string caseNumber, CancellationToken cancellationToken)
        {
            return Task.FromResult(CaseDetails);
        }
    }

    private sealed class FakeLumoraClient : ILumoraClient
    {
        private readonly LumoraClientResult<LumoraOrderDiagnosticResponse> _orderDiagnosticResult;

        public FakeLumoraClient()
            : this(LumoraClientResult<LumoraOrderDiagnosticResponse>.Failure("not_configured", "not configured"))
        {
        }

        public FakeLumoraClient(LumoraClientResult<LumoraOrderDiagnosticResponse> orderDiagnosticResult)
        {
            _orderDiagnosticResult = orderDiagnosticResult;
        }

        public int OrderDiagnosticCalls { get; private set; }

        public Task<LumoraClientResult<LumoraHealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LumoraClientResult<LumoraOrderDiagnosticResponse>> GetOrderDiagnosticAsync(
            string orderId,
            CancellationToken cancellationToken = default)
        {
            OrderDiagnosticCalls++;
            return Task.FromResult(_orderDiagnosticResult);
        }

        public Task<LumoraClientResult<LumoraOrderTriageCandidatesResponse>> GetTriageCandidatesAsync(
            int? lookbackMinutes = null,
            int? limit = null,
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

    private sealed class FakeActionRequestService : IActionRequestService
    {
        public List<CreateCustomerMessageActionRequest> CreatedRequests { get; } = [];

        public IReadOnlyList<ActionRequestDetails> PendingActions { get; init; } = [];

        public ActionRequestDetails? GetByPublicIdResult { get; init; }

        public ActionRequestDetails? ApproveResult { get; init; }

        public ActionRequestDetails? CancelResult { get; init; }

        public string? ApprovedPublicId { get; private set; }

        public long? ApprovedByChatId { get; private set; }

        public string? CancelledPublicId { get; private set; }

        public long? CancelledByChatId { get; private set; }

        public bool EmailWasSent { get; private set; }

        public Task<ActionRequestDetails> CreateCustomerMessageEmailAsync(
            CreateCustomerMessageActionRequest request,
            CancellationToken cancellationToken = default)
        {
            CreatedRequests.Add(request);
            return Task.FromResult(CreateActionDetails("ACT-00001", ActionRequestStatuses.PendingApproval, request.OrderId));
        }

        public Task<IReadOnlyList<ActionRequestDetails>> ListPendingAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PendingActions);
        }

        public Task<ActionRequestDetails?> GetByPublicIdAsync(
            string publicId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetByPublicIdResult);
        }

        public Task<ActionRequestDetails?> ApproveAsync(
            string publicId,
            long approvedByChatId,
            CancellationToken cancellationToken = default)
        {
            ApprovedPublicId = publicId;
            ApprovedByChatId = approvedByChatId;
            return Task.FromResult(ApproveResult);
        }

        public Task<ActionRequestDetails?> CancelAsync(
            string publicId,
            long cancelledByChatId,
            CancellationToken cancellationToken = default)
        {
            CancelledPublicId = publicId;
            CancelledByChatId = cancelledByChatId;
            return Task.FromResult(CancelResult);
        }
    }

    private sealed class FakeOrderTriageService : IOrderTriageService
    {
        public IReadOnlyList<OrderTriageSnapshotDetails> Snapshots { get; init; } = [];

        public Task<OrderTriageRefreshResult> RefreshAsync(
            Guid clientApplicationId,
            int? lookbackMinutes = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OrderTriageRefreshResult(0, 0, 0));
        }

        public Task<IReadOnlyList<OrderTriageSnapshotDetails>> GetTopAsync(
            int limit,
            int? cursor = null,
            CancellationToken cancellationToken = default)
        {
            var skip = Math.Max(0, cursor ?? 0);
            return Task.FromResult<IReadOnlyList<OrderTriageSnapshotDetails>>(
                Snapshots.Skip(skip).Take(limit).ToList());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
