using CommerceOps.Domain;
using Microsoft.EntityFrameworkCore;

namespace CommerceOps.Infrastructure.Persistence;

public sealed class CommerceOpsDbContext(DbContextOptions<CommerceOpsDbContext> options) : DbContext(options)
{
    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();
    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();
    public DbSet<OperationalCase> OperationalCases => Set<OperationalCase>();
    public DbSet<CaseFinding> CaseFindings => Set<CaseFinding>();
    public DbSet<ActionRequest> ActionRequests => Set<ActionRequest>();
    public DbSet<OrderTriageSnapshot> OrderTriageSnapshots => Set<OrderTriageSnapshot>();

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

        modelBuilder.Entity<OperationalCase>(entity =>
        {
            entity.ToTable("operational_cases");

            entity.HasKey(operationalCase => operationalCase.Id);
            entity.Property(operationalCase => operationalCase.Id).ValueGeneratedNever();
            entity.Property(operationalCase => operationalCase.CaseNumber).HasMaxLength(32).IsRequired();
            entity.Property(operationalCase => operationalCase.ProblemType).HasMaxLength(120).IsRequired();
            entity.Property(operationalCase => operationalCase.Title).HasMaxLength(200).IsRequired();
            entity.Property(operationalCase => operationalCase.Summary).HasMaxLength(1000).IsRequired();
            entity.Property(operationalCase => operationalCase.Status).HasMaxLength(32).IsRequired();
            entity.Property(operationalCase => operationalCase.RiskLevel).HasMaxLength(32).IsRequired();
            entity.Property(operationalCase => operationalCase.RiskScore).IsRequired();
            entity.Property(operationalCase => operationalCase.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(operationalCase => operationalCase.EntityId).HasMaxLength(160).IsRequired();
            entity.Property(operationalCase => operationalCase.CreatedAt).IsRequired();
            entity.Property(operationalCase => operationalCase.UpdatedAt).IsRequired();

            entity.HasOne(operationalCase => operationalCase.ClientApplication)
                .WithMany(application => application.OperationalCases)
                .HasForeignKey(operationalCase => operationalCase.ClientApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(operationalCase => operationalCase.CaseNumber).IsUnique();
            entity.HasIndex(operationalCase => operationalCase.ClientApplicationId);
            entity.HasIndex(operationalCase => new
            {
                operationalCase.ClientApplicationId,
                operationalCase.EntityType,
                operationalCase.EntityId,
                operationalCase.ProblemType,
                operationalCase.Status
            }).HasDatabaseName("IX_operational_cases_open_lookup");
            entity.HasIndex(operationalCase => operationalCase.CreatedAt);
        });

        modelBuilder.Entity<CaseFinding>(entity =>
        {
            entity.ToTable("case_findings");

            entity.HasKey(finding => finding.Id);
            entity.Property(finding => finding.Id).ValueGeneratedNever();
            entity.Property(finding => finding.Type).HasMaxLength(120).IsRequired();
            entity.Property(finding => finding.Severity).HasMaxLength(32).IsRequired();
            entity.Property(finding => finding.Title).HasMaxLength(200).IsRequired();
            entity.Property(finding => finding.Description).HasMaxLength(1000).IsRequired();
            entity.Property(finding => finding.EvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.Property(finding => finding.CreatedAt).IsRequired();

            entity.HasOne(finding => finding.Case)
                .WithMany(operationalCase => operationalCase.Findings)
                .HasForeignKey(finding => finding.CaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(finding => finding.CaseId);
            entity.HasIndex(finding => finding.Type);
        });

        modelBuilder.Entity<ActionRequest>(entity =>
        {
            entity.ToTable("action_requests");

            entity.HasKey(actionRequest => actionRequest.Id);
            entity.Property(actionRequest => actionRequest.Id).ValueGeneratedNever();
            entity.Property(actionRequest => actionRequest.PublicId).HasMaxLength(32).IsRequired();
            entity.Property(actionRequest => actionRequest.Type).HasMaxLength(120).IsRequired();
            entity.Property(actionRequest => actionRequest.Status).HasMaxLength(32).IsRequired();
            entity.Property(actionRequest => actionRequest.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(actionRequest => actionRequest.EntityId).HasMaxLength(160).IsRequired();
            entity.Property(actionRequest => actionRequest.Risk).HasMaxLength(32);
            entity.Property(actionRequest => actionRequest.Reason).HasMaxLength(200).IsRequired();
            entity.Property(actionRequest => actionRequest.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(actionRequest => actionRequest.CreatedByChatId).IsRequired();
            entity.Property(actionRequest => actionRequest.CreatedAt).IsRequired();
            entity.Property(actionRequest => actionRequest.FailureReason).HasMaxLength(1000);

            entity.HasIndex(actionRequest => actionRequest.PublicId).IsUnique();
            entity.HasIndex(actionRequest => actionRequest.Status);
            entity.HasIndex(actionRequest => actionRequest.CreatedAt);
            entity.HasIndex(actionRequest => new
            {
                actionRequest.EntityType,
                actionRequest.EntityId,
                actionRequest.Status
            }).HasDatabaseName("IX_action_requests_entity_status");
        });

        modelBuilder.Entity<OrderTriageSnapshot>(entity =>
        {
            entity.ToTable("order_triage_snapshots");

            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Id).ValueGeneratedNever();
            entity.Property(snapshot => snapshot.OrderId).HasMaxLength(160).IsRequired();
            entity.Property(snapshot => snapshot.OrderNumber).HasMaxLength(80);
            entity.Property(snapshot => snapshot.RiskScore).IsRequired();
            entity.Property(snapshot => snapshot.RiskLevel).HasMaxLength(32).IsRequired();
            entity.Property(snapshot => snapshot.LastFindingCode).HasMaxLength(120);
            entity.Property(snapshot => snapshot.Summary).HasMaxLength(1000);
            entity.Property(snapshot => snapshot.OrderStatus).HasMaxLength(80).IsRequired();
            entity.Property(snapshot => snapshot.PaymentStatus).HasMaxLength(80);
            entity.Property(snapshot => snapshot.TotalValue).HasPrecision(18, 2);
            entity.Property(snapshot => snapshot.SourceUpdatedAt).IsRequired();
            entity.Property(snapshot => snapshot.RefreshedAt).IsRequired();
            entity.Property(snapshot => snapshot.Notified).IsRequired();

            entity.HasOne(snapshot => snapshot.ClientApplication)
                .WithMany(application => application.OrderTriageSnapshots)
                .HasForeignKey(snapshot => snapshot.ClientApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(snapshot => new
            {
                snapshot.ClientApplicationId,
                snapshot.OrderId
            }).IsUnique();
            entity.HasIndex(snapshot => snapshot.RiskScore)
                .IsDescending();
            entity.HasIndex(snapshot => snapshot.RiskLevel);
            entity.HasIndex(snapshot => snapshot.RefreshedAt);
        });
    }
}
