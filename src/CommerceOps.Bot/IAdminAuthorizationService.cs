namespace CommerceOps.Bot;

public interface IAdminAuthorizationService
{
    bool IsAuthorized(long telegramUserId);
}
