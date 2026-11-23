using AccessRequestHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestHub.Infrastructure;

public sealed class HubDbContext(DbContextOptions<HubDbContext> options) : DbContext(options)
{
    public DbSet<DemoUser> Users => Set<DemoUser>();
    public DbSet<ManagedApplication> Applications => Set<ManagedApplication>();
    public DbSet<AccessRequest> Requests => Set<AccessRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var users = model.Entity<DemoUser>();
        users.HasIndex(u => u.Email).IsUnique();
        users.Property(u => u.Role).HasConversion<string>();
        users.HasOne<DemoUser>().WithMany().HasForeignKey(u => u.ManagerId).OnDelete(DeleteBehavior.Restrict);
        users.HasData(
            new DemoUser { Id = 1, Email = "alice@example.local", Role = DemoRole.Requester, ManagerId = 2 },
            new DemoUser { Id = 2, Email = "bob@example.local", Role = DemoRole.Manager },
            new DemoUser { Id = 3, Email = "carol@example.local", Role = DemoRole.SystemOwner },
            new DemoUser { Id = 4, Email = "dana@example.local", Role = DemoRole.SystemOwner },
            new DemoUser { Id = 5, Email = "erin@example.local", Role = DemoRole.Auditor });

        var apps = model.Entity<ManagedApplication>();
        apps.HasIndex(a => a.Name).IsUnique();
        apps.HasOne<DemoUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
        apps.HasData(
            new ManagedApplication { Id = 1, Name = "CRM", OwnerId = 3 },
            new ManagedApplication { Id = 2, Name = "Finance Portal", OwnerId = 4 });

        var requests = model.Entity<AccessRequest>();
        requests.HasIndex(r => new { r.RequesterId, r.ClientRequestId }).IsUnique();
        requests.HasIndex(r => new { r.ApplicationId, r.Status });
        requests.HasOne(r => r.Requester).WithMany().HasForeignKey(r => r.RequesterId).OnDelete(DeleteBehavior.Restrict);
        requests.HasOne(r => r.Application).WithMany().HasForeignKey(r => r.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        requests.Property(r => r.Environment).HasConversion<string>();
        requests.Property(r => r.AccessLevel).HasConversion<string>();
        requests.Property(r => r.Status).HasConversion<string>();
        requests.Property(r => r.Version).IsConcurrencyToken();
        requests.ToTable("Requests", table =>
        {
            table.HasCheckConstraint("CK_Requests_Environment", "Environment IN ('NonProduction', 'Production')");
            table.HasCheckConstraint("CK_Requests_AccessLevel", "AccessLevel IN ('Read', 'Admin')");
            table.HasCheckConstraint("CK_Requests_Status", "Status IN ('PendingManager', 'PendingSystemOwner', 'Approved', 'Rejected')");
            table.HasCheckConstraint("CK_Requests_Required", "Version > 0 AND length(trim(Justification)) BETWEEN 1 AND 2000 AND length(trim(PolicyVersion)) > 0");
        });

        var audit = model.Entity<AuditEvent>();
        audit.HasIndex(e => new { e.RequestId, e.RequestVersion }).IsUnique();
        audit.HasOne<AccessRequest>().WithMany().HasForeignKey(e => e.RequestId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne(e => e.Actor).WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        audit.Property(e => e.PreviousStatus).HasConversion<string>();
        audit.Property(e => e.ResultingStatus).HasConversion<string>();
        audit.Property(e => e.Action).HasConversion<string>();
        audit.ToTable("AuditEvents", table =>
        {
            table.HasCheckConstraint("CK_AuditEvents_Reason", "Action <> 'Rejected' OR (Reason IS NOT NULL AND length(trim(Reason)) BETWEEN 1 AND 1000)");
            table.HasCheckConstraint("CK_AuditEvents_Action", "Action IN ('Created', 'Approved', 'Rejected') AND RequestVersion > 0");
        });
    }
}
