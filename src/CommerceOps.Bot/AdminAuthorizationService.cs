using Microsoft.Extensions.Options;

namespace CommerceOps.Bot;

public sealed class AdminAuthorizationService(IOptions<TelegramBotOptions> options) : IAdminAuthorizationService
{
    private readonly HashSet<long> _allowedAdminIds = options.Value.AllowedAdminIds.ToHashSet();

    public bool IsAuthorized(long telegramUserId)
    {
        return _allowedAdminIds.Contains(telegramUserId);
    }
}
