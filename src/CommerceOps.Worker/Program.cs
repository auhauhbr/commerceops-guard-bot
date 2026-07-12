using CommerceOps.Worker;
using CommerceOps.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCommerceOpsInfrastructure(builder.Configuration);
builder.Services.Configure<TriageRefreshOptions>(options =>
{
    var section = builder.Configuration.GetSection(TriageRefreshOptions.SectionName);
    options.Enabled = TryGetBool(builder.Configuration["TRIAGE_REFRESH_ENABLED"] ?? section["Enabled"])
        ?? builder.Environment.IsDevelopment();
    options.IntervalSeconds = TryGetPositiveInt(
        builder.Configuration["TRIAGE_REFRESH_INTERVAL_SECONDS"] ?? section["IntervalSeconds"],
        300);
    options.LookbackMinutes = TryGetPositiveInt(
        builder.Configuration["TRIAGE_REFRESH_LOOKBACK_MINUTES"] ?? section["LookbackMinutes"],
        240);
    options.Limit = TryGetPositiveInt(
        builder.Configuration["TRIAGE_REFRESH_LIMIT"] ?? section["Limit"],
        100);
    options.ClientApplicationPublicId =
        builder.Configuration["TRIAGE_REFRESH_CLIENT_PUBLIC_ID"]
        ?? section["ClientApplicationPublicId"]
        ?? builder.Configuration["ClientApplicationSeed:PublicId"]
        ?? "lumora";
});
builder.Services.AddHostedService<TriageRefreshWorker>();

var host = builder.Build();
host.Run();

static bool? TryGetBool(string? value)
{
    return bool.TryParse(value, out var parsed) ? parsed : null;
}

static int TryGetPositiveInt(string? value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
