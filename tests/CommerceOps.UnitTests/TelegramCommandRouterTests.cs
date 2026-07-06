using CommerceOps.Application.Cases;
using CommerceOps.Bot;

namespace CommerceOps.UnitTests;

public sealed class TelegramCommandRouterTests
{
    [Fact]
    public async Task RouteAsyncBlocksUnauthorizedUser()
    {
        var router = new TelegramCommandRouter(new FakeCaseQueryService(), new FakeAuthorizationService(false));

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
        var router = new TelegramCommandRouter(queryService, new FakeAuthorizationService(true));

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
        var router = new TelegramCommandRouter(queryService, new FakeAuthorizationService(true));

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
        var router = new TelegramCommandRouter(queryService, new FakeAuthorizationService(true));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/resumo"), CancellationToken.None);

        Assert.Contains("Casos abertos: 2", response);
        Assert.Contains("medium: 2", response);
        Assert.Contains("open: 2", response);
    }

    [Fact]
    public async Task RouteAsyncReturnsHelpForStart()
    {
        var router = new TelegramCommandRouter(new FakeCaseQueryService(), new FakeAuthorizationService(true));

        var response = await router.RouteAsync(new TelegramCommandContext(1, 123, "/start"), CancellationToken.None);

        Assert.Contains("CommerceOps Guard", response);
        Assert.Contains("/casos", response);
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
}
