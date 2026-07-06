using CommerceOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommerceOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCommerceOpsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IOptions<ClientApplicationSeedOptions>>(_ =>
        {
            var section = configuration.GetSection(ClientApplicationSeedOptions.SectionName);
            var defaults = new ClientApplicationSeedOptions();

            return Options.Create(new ClientApplicationSeedOptions
            {
                Name = section["Name"] ?? defaults.Name,
                PublicId = section["PublicId"] ?? defaults.PublicId,
                Secret = section["Secret"] ?? defaults.Secret,
                BaseUrl = section["BaseUrl"],
                TelegramChannel = section["TelegramChannel"],
                IsActive = bool.TryParse(section["IsActive"], out var isActive) ? isActive : defaults.IsActive
            });
        });

        services.AddDbContext<CommerceOpsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("CommerceOps")));

        services.AddScoped<ClientApplicationSeeder>();

        return services;
    }
}
