using CommerceOps.Domain;

namespace CommerceOps.Application.Cases;

public interface ICaseService
{
    Task EvaluateOperationalEventAsync(OperationalEvent operationalEvent, CancellationToken cancellationToken);
}
