using CommerceOps.Application.Cases;
using CommerceOps.Application.Actions;
using CommerceOps.Application.Lumora;
using CommerceOps.Application.Triage;
using CommerceOps.Infrastructure.Lumora;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IOptions<LumoraOptions>>(_ =>
        {
            var section = configuration.GetSection(LumoraOptions.SectionName);
            var defaults = new LumoraOptions();
            var configuredTimeout = configuration["LUMORA_HTTP_TIMEOUT_SECONDS"] ?? section["TimeoutSeconds"];

            return Options.Create(new LumoraOptions
            {
                AppId = configuration["LUMORA_APP_ID"] ?? section["AppId"] ?? defaults.AppId,
                BaseUrl = configuration["LUMORA_BASE_URL"] ?? section["BaseUrl"] ?? defaults.BaseUrl,
                SharedSecret = configuration["LUMORA_SHARED_SECRET"] ?? section["SharedSecret"] ?? defaults.SharedSecret,
                TimeoutSeconds = int.TryParse(configuredTimeout, out var timeoutSeconds)
                    ? timeoutSeconds
                    : defaults.TimeoutSeconds
            });
        });

        services.AddHttpClient<ILumoraClient, LumoraClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LumoraOptions>>().Value;

            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                httpClient.BaseAddress = baseUri;
            }

            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });

        services.AddScoped<CaseRuleEvaluator>();
        services.AddScoped<IOrderRiskScorer, OrderRiskScorer>();
        services.AddScoped<ICaseService, CaseService>();
        services.AddScoped<IOperationalCaseQueryService, OperationalCaseQueryService>();
        services.AddScoped<IActionRequestService, ActionRequestService>();
        services.AddScoped<IOrderTriageService, OrderTriageService>();
        services.AddScoped<ClientApplicationSeeder>();

        return services;
    }
}
