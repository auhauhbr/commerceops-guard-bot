using CommerceOps.Bot;
using Microsoft.Extensions.Options;

namespace CommerceOps.UnitTests;

public sealed class AdminAuthorizationServiceTests
{
    [Fact]
    public void IsAuthorizedReturnsTrueForConfiguredAdmin()
    {
        var service = CreateService([123, 456]);

        Assert.True(service.IsAuthorized(456));
    }

    [Fact]
    public void IsAuthorizedReturnsFalseForUnknownUser()
    {
        var service = CreateService([123]);

        Assert.False(service.IsAuthorized(999));
    }

    [Fact]
    public void ParseAllowedAdminIdsAcceptsCommaAndWhitespaceSeparatedValues()
    {
        var ids = TelegramBotOptions.ParseAllowedAdminIds("123, 456 789;123");

        Assert.Equal([123, 456, 789], ids);
    }

    private static AdminAuthorizationService CreateService(List<long> allowedAdminIds)
    {
        return new AdminAuthorizationService(Options.Create(new TelegramBotOptions
        {
            AllowedAdminIds = allowedAdminIds
        }));
    }
}
