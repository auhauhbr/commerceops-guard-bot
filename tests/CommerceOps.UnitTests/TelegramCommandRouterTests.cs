using CommerceOps.Application.Actions;
using CommerceOps.Application.Cases;
using CommerceOps.Application.Lumora;
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

        Assert.Contains("Pedido #1 — Diagnóstico Lumora", response);
        Assert.Contains("Status do pedido: pending_payment", response);
        Assert.Contains("Pagamento: pending", response);
        Assert.Contains("Estoque: ok", response);
        Assert.Contains("Risco: low", response);
        Assert.Contains("1. pending_order_without_approved_payment", response);
        Assert.Contains("Order is pending and does not have an approved payment.", response);
        Assert.Contains("- Ponto De Acesso Ubiquiti UniFi U6+ Wi-Fi 6 Interno", response);
        Assert.Contains("qtd: 1", response);
        Assert.Contains("total: R$ 899.00", response);
        Assert.Contains("estoque atual: 8", response);
        Assert.Contains("Use /mensagem-pedido 1 para gerar um rascunho de mensagem ao cliente.", response);
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

        Assert.Contains("Rascunho de mensagem para cliente — Pedido #1", response);
        Assert.Contains("Canal sugerido: email", response);
        Assert.Contains("Assunto: Atualização sobre seu pedido #1", response);
        Assert.Contains("Mensagem:", response);
        Assert.Contains("Status:", response);
        Assert.Contains("Rascunho gerado. Nenhuma mensagem foi enviada.", response);
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
        Assert.Contains("Você não precisa refazer o pedido", response);
    }

    [Fact]
    public async Task RouteAsyncHelpIncludesNewOrderCommands()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/help"), CancellationToken.None);

        Assert.Contains("/pedido {id} - consulta diagnóstico operacional de um pedido da Lumora", response);
        Assert.Contains("/mensagem-pedido {id} - gera rascunho de mensagem para o cliente, sem enviar", response);
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

        Assert.Contains("Ação pendente criada: ACT-00001", response);
        Assert.Contains("Tipo: customer_message_email", response);
        Assert.Contains("Pedido: #1", response);
        Assert.Contains("Risco: low", response);
        Assert.Contains("Status: pending_approval", response);
        Assert.Contains("Assunto:", response);
        Assert.Contains("Atualização sobre seu pedido #1", response);
        Assert.Contains("Nenhuma mensagem foi enviada.", response);
        Assert.Contains("/confirmar-acao ACT-00001", response);
        Assert.Contains("/cancelar-acao ACT-00001", response);
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
        Assert.Contains("ACT-00001", response);
        Assert.Contains("Tipo: customer_message_email", response);
        Assert.Contains("Pedido: #1", response);
        Assert.Contains("Status: pending_approval", response);
        Assert.Contains("Criada em:", response);
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

        Assert.Contains("/preparar-mensagem-pedido {id} - cria ação pendente com rascunho de mensagem ao cliente", response);
        Assert.Contains("/acoes - lista ações pendentes", response);
        Assert.Contains("/confirmar-acao {id} - aprova uma ação pendente", response);
        Assert.Contains("/cancelar-acao {id} - cancela uma ação pendente", response);
    }

    private static TelegramCommandRouter CreateRouter(
        IOperationalCaseQueryService? queryService = null,
        bool isAuthorized = true,
        ILumoraClient? lumoraClient = null,
        IActionRequestService? actionRequestService = null)
    {
        return new TelegramCommandRouter(
            queryService ?? new FakeCaseQueryService(),
            new FakeAuthorizationService(isAuthorized),
            lumoraClient ?? new FakeLumoraClient(),
            new CustomerMessageDraftService(),
            actionRequestService ?? new FakeActionRequestService());
    }

    private static LumoraOrderDiagnosticResponse CreateDiagnostic(
        string findingType = "pending_order_without_approved_payment")
    {
        return new LumoraOrderDiagnosticResponse(
            "1",
            "pending_payment",
            "pending",
            "ok",
            [
                new LumoraDiagnosticFinding(
                    findingType,
                    "info",
                    "Order is pending and does not have an approved payment.",
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
}
