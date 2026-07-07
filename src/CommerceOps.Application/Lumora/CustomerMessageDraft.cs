namespace CommerceOps.Application.Lumora;

public enum NotificationChannel
{
    Email
}

public sealed record CustomerMessageDraft(
    string OrderId,
    NotificationChannel Channel,
    string Subject,
    string Body,
    string Reason,
    string? Risk,
    IReadOnlyList<string> Findings,
    string Warning);

public interface ICustomerMessageDraftComposer
{
    CustomerMessageDraft Compose(LumoraOrderDiagnosticResponse diagnostic);
}

public sealed class CustomerMessageDraftService : ICustomerMessageDraftComposer
{
    public CustomerMessageDraft Compose(LumoraOrderDiagnosticResponse diagnostic)
    {
        var orderReference = GetOrderReference(diagnostic);
        var findingTypes = diagnostic.Findings
            .Select(finding => finding.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToList();
        var primaryFinding = findingTypes.FirstOrDefault() ?? "generic_order_review";
        var subject = CreateSubject(primaryFinding, orderReference);
        var body = CreateBody(primaryFinding, orderReference);

        return new CustomerMessageDraft(
            diagnostic.OrderId,
            NotificationChannel.Email,
            subject,
            body,
            primaryFinding,
            diagnostic.Risk,
            findingTypes,
            "Rascunho gerado. Nenhuma mensagem foi enviada.");
    }

    private static string GetOrderReference(LumoraOrderDiagnosticResponse diagnostic)
    {
        return string.IsNullOrWhiteSpace(diagnostic.OrderNumber)
            ? diagnostic.OrderId
            : diagnostic.OrderNumber;
    }

    private static string CreateSubject(string findingType, string orderReference)
    {
        return findingType switch
        {
            "order_paid_but_pending" => $"Estamos revisando seu pedido #{orderReference}",
            "cancelled_order_with_approved_payment" => $"Revisão necessária no seu pedido #{orderReference}",
            _ => $"Atualização sobre seu pedido #{orderReference}"
        };
    }

    private static string CreateBody(string findingType, string orderReference)
    {
        return findingType switch
        {
            "pending_order_without_approved_payment" => $"""
                Olá! Identificamos que seu pedido #{orderReference} foi criado com sucesso e ainda está aguardando confirmação de pagamento.

                Se você já concluiu o pagamento, aguarde alguns instantes para a atualização automática. Caso o status não mude, entre em contato com o suporte informando o número do pedido.

                Atenciosamente,
                Equipe Lumora
                """,
            "order_paid_but_pending" => $"""
                Olá! Identificamos que o pagamento do seu pedido #{orderReference} foi aprovado, mas o pedido ainda não avançou automaticamente em nosso sistema.

                Nossa equipe já pode revisar essa inconsistência operacional. Você não precisa refazer o pedido neste momento.

                Atenciosamente,
                Equipe Lumora
                """,
            "cancelled_order_with_approved_payment" => $"""
                Olá! Identificamos uma inconsistência entre o status do pagamento e o status do pedido #{orderReference}.

                Nossa equipe precisa revisar o caso para confirmar os próximos passos. Se necessário, entraremos em contato com mais informações.

                Atenciosamente,
                Equipe Lumora
                """,
            _ => $"""
                Olá! Estamos revisando uma atualização operacional relacionada ao seu pedido #{orderReference}.

                Caso precise de ajuda, entre em contato com o suporte informando o número do pedido.

                Atenciosamente,
                Equipe Lumora
                """
        };
    }
}
