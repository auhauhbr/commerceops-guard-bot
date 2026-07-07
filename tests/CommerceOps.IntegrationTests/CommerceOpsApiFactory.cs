using CommerceOps.Domain;
using CommerceOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommerceOps.IntegrationTests;

public sealed class CommerceOpsApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public const string AppPublicId = "lumora";
    public const string AppSecret = "integration-test-secret";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientApplicationSeed:Name"] = "Lumora",
                ["ClientApplicationSeed:PublicId"] = AppPublicId,
                ["ClientApplicationSeed:Secret"] = AppSecret,
                ["ClientApplicationSeed:IsActive"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CommerceOpsDbContext>>();

            services.AddDbContext<CommerceOpsDbContext>(options =>
                options.UseSqlite(_connection));

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CommerceOpsDbContext>();
            dbContext.Database.EnsureCreated();

            if (!dbContext.ClientApplications.Any(application => application.PublicId == AppPublicId))
            {
                dbContext.ClientApplications.Add(new ClientApplication
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Lumora",
                    PublicId = AppPublicId,
                    Secret = AppSecret,
                    BaseUrl = "http://localhost",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                dbContext.SaveChanges();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
