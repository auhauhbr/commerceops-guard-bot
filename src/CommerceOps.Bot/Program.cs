using CommerceOps.Bot;
using CommerceOps.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCommerceOpsInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<TelegramBotOptions>(options =>
{
    var section = builder.Configuration.GetSection(TelegramBotOptions.SectionName);
    options.BotToken = section["BotToken"];
    options.AllowedAdminIds = section.GetSection("AllowedAdminIds")
        .GetChildren()
        .Select(child => long.TryParse(child.Value, out var id) ? id : (long?)null)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .ToList();

    options.BotToken = ReadSetting("TELEGRAM_BOT_TOKEN", options.BotToken);

    var allowedAdminIds = ReadSetting("TELEGRAM_ALLOWED_ADMIN_IDS", section["AllowedAdminIds"]);
    if (!string.IsNullOrWhiteSpace(allowedAdminIds))
    {
        options.AllowedAdminIds = TelegramBotOptions.ParseAllowedAdminIds(allowedAdminIds);
    }
});

builder.Services.AddSingleton<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<TelegramCommandRouter>();
builder.Services.AddHostedService<TelegramBotService>();

var host = builder.Build();
host.Run();

static string? ReadSetting(string key, string? fallback)
{
    return Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;
}
