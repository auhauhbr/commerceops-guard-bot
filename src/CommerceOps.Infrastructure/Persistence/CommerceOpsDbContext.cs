using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class CommerceOpsDbContext(DbContextOptions<CommerceOpsDbContext> options) : DbContext(options)
{
    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();
    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientApplication>(entity =>
        {
            entity.ToTable("client_applications");

            entity.HasKey(application => application.Id);
            entity.Property(application => application.Id).ValueGeneratedNever();
            entity.Property(application => application.Name).HasMaxLength(160).IsRequired();
            entity.Property(application => application.PublicId).HasMaxLength(80).IsRequired();
            entity.Property(application => application.Secret).HasMaxLength(256).IsRequired();
            entity.Property(application => application.BaseUrl).HasMaxLength(500);
            entity.Property(application => application.TelegramChannel).HasMaxLength(160);
            entity.Property(application => application.IsActive).IsRequired();
            entity.Property(application => application.CreatedAt).IsRequired();
            entity.HasIndex(application => application.PublicId).IsUnique();
        });

        modelBuilder.Entity<OperationalEvent>(entity =>
        {
            entity.ToTable("operational_events");

            entity.HasKey(operationalEvent => operationalEvent.Id);
            entity.Property(operationalEvent => operationalEvent.Id).ValueGeneratedNever();
            entity.Property(operationalEvent => operationalEvent.EventType).HasMaxLength(120).IsRequired();
            entity.Property(operationalEvent => operationalEvent.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(operationalEvent => operationalEvent.EntityId).HasMaxLength(160).IsRequired();
            entity.Property(operationalEvent => operationalEvent.OccurredAt).IsRequired();
            entity.Property(operationalEvent => operationalEvent.Severity).HasMaxLength(32).IsRequired();
            entity.Property(operationalEvent => operationalEvent.RawBody).IsRequired();
            entity.Property(operationalEvent => operationalEvent.DataJson).HasColumnType("jsonb");
            entity.Property(operationalEvent => operationalEvent.ReceivedAt).IsRequired();

            entity.HasOne(operationalEvent => operationalEvent.ClientApplication)
                .WithMany(application => application.OperationalEvents)
                .HasForeignKey(operationalEvent => operationalEvent.ClientApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(operationalEvent => operationalEvent.ClientApplicationId);
            entity.HasIndex(operationalEvent => operationalEvent.EventType);
            entity.HasIndex(operationalEvent => operationalEvent.ReceivedAt);
        });
    }
}
