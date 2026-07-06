using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class ClientApplicationSeeder(
    CommerceOpsDbContext dbContext,
    IOptions<ClientApplicationSeedOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;

        if (string.IsNullOrWhiteSpace(seed.PublicId) || string.IsNullOrWhiteSpace(seed.Secret))
        {
            return;
        }

        var existingApplication = await dbContext.ClientApplications
            .SingleOrDefaultAsync(application => application.PublicId == seed.PublicId, cancellationToken);

        if (existingApplication is not null)
        {
            existingApplication.Name = seed.Name;
            existingApplication.BaseUrl = seed.BaseUrl;
            existingApplication.TelegramChannel = seed.TelegramChannel;
            existingApplication.IsActive = seed.IsActive;
            existingApplication.Secret = seed.Secret;
        }
        else
        {
            dbContext.ClientApplications.Add(new ClientApplication
            {
                Id = Guid.NewGuid(),
                Name = seed.Name,
                PublicId = seed.PublicId,
                Secret = seed.Secret,
                BaseUrl = seed.BaseUrl,
                TelegramChannel = seed.TelegramChannel,
                IsActive = seed.IsActive,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
