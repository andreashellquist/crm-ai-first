using CrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<WorkspaceSettings> WorkspaceSettings => Set<WorkspaceSettings>();
    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<PipelineSnapshot> PipelineSnapshots => Set<PipelineSnapshot>();
    public DbSet<ForecastSnapshot> ForecastSnapshots => Set<ForecastSnapshot>();
    public DbSet<ActivityMetric> ActivityMetrics => Set<ActivityMetric>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<WorkspaceMember>(e =>
        {
            e.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
            // Explicit defaults matter here — without them, EF Core's
            // migration generator backfills existing rows with the CLR
            // default (false / DateTime.MinValue), which for IsActive would
            // lock out every already-provisioned member the moment this
            // column's migration ran.
            e.Property(m => m.IsActive).HasDefaultValue(true);
            e.Property(m => m.UpdatedAt).HasDefaultValueSql("now()");
            e.HasOne(m => m.Workspace).WithMany(w => w.Members)
                .HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.HasIndex(c => new { c.WorkspaceId, c.CompanyId });
            e.HasIndex(c => new { c.WorkspaceId, c.Email });
            e.Property(c => c.CustomFields).HasDefaultValue("{}");
            e.HasOne(c => c.Workspace).WithMany(w => w.Contacts)
                .HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Company).WithMany(co => co.Contacts)
                .HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.HasIndex(c => new { c.WorkspaceId, c.Name });
            e.Property(c => c.CustomFields).HasDefaultValue("{}");
            e.HasOne(c => c.Workspace).WithMany(w => w.Companies)
                .HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Pipeline>(e =>
        {
            e.HasOne(p => p.Workspace).WithMany(w => w.Pipelines)
                .HasForeignKey(p => p.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stage>(e =>
        {
            e.HasIndex(s => new { s.PipelineId, s.Order });
            e.HasOne(s => s.Pipeline).WithMany(p => p.Stages)
                .HasForeignKey(s => s.PipelineId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Deal>(e =>
        {
            e.HasIndex(d => new { d.WorkspaceId, d.StageId });
            e.HasIndex(d => new { d.WorkspaceId, d.PipelineId });
            e.Property(d => d.CustomFields).HasDefaultValue("{}");
            e.HasOne(d => d.Workspace).WithMany(w => w.Deals)
                .HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Pipeline).WithMany(p => p.Deals)
                .HasForeignKey(d => d.PipelineId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Stage).WithMany(s => s.Deals)
                .HasForeignKey(d => d.StageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Company).WithMany(c => c.Deals)
                .HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(d => d.Contacts).WithMany(c => c.Deals)
                .UsingEntity(j => j.ToTable("DealContacts"));
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.HasIndex(a => new { a.WorkspaceId, a.DealId, a.CreatedAt });
            e.HasIndex(a => new { a.WorkspaceId, a.ContactId, a.CreatedAt });
            e.HasOne(a => a.Workspace).WithMany(w => w.Activities)
                .HasForeignKey(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Contact).WithMany(c => c.Activities)
                .HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Company).WithMany(c => c.Activities)
                .HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Deal).WithMany(d => d.Activities)
                .HasForeignKey(a => a.DealId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Job>(e =>
        {
            e.HasIndex(j => new { j.Status, j.RunAt });
            e.HasIndex(j => new { j.WorkspaceId, j.Status });
        });

        modelBuilder.Entity<WorkspaceSettings>(e =>
        {
            e.HasIndex(s => s.WorkspaceId).IsUnique();
            e.Property(s => s.Terminology).HasDefaultValue("{}");
            e.HasOne(s => s.Workspace).WithOne(w => w.Settings)
                .HasForeignKey<WorkspaceSettings>(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FieldDefinition>(e =>
        {
            // One key per entity type per workspace — inserting a duplicate
            // FieldDefinition row is a conflict, not a silent overwrite.
            e.HasIndex(f => new { f.WorkspaceId, f.EntityType, f.Key }).IsUnique();
            e.HasOne(f => f.Workspace).WithMany(w => w.FieldDefinitions)
                .HasForeignKey(f => f.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.HasIndex(t => new { t.WorkspaceId, t.CompletedAt, t.DueAt }); // open-tasks queries
            e.HasIndex(t => new { t.WorkspaceId, t.DealId });
            e.HasIndex(t => new { t.WorkspaceId, t.ContactId });
            e.HasOne(t => t.Workspace).WithMany(w => w.Tasks)
                .HasForeignKey(t => t.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Contact).WithMany(c => c.Tasks)
                .HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Company).WithMany(c => c.Tasks)
                .HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Deal).WithMany(d => d.Tasks)
                .HasForeignKey(t => t.DealId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            // The notification center's two read patterns: "recent for me in
            // this workspace" and "unread count for me in this workspace".
            e.HasIndex(n => new { n.WorkspaceId, n.UserId, n.ReadAt, n.CreatedAt });
            e.HasOne(n => n.Workspace).WithMany(w => w.Notifications)
                .HasForeignKey(n => n.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.User).WithMany()
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreference>(e =>
        {
            e.HasIndex(p => new { p.UserId, p.Type }).IsUnique();
            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedView>(e =>
        {
            // "My saved views for this list" — the one read pattern.
            e.HasIndex(v => new { v.WorkspaceId, v.UserId, v.EntityType });
            e.HasOne(v => v.Workspace).WithMany(w => w.SavedViews)
                .HasForeignKey(v => v.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.User).WithMany()
                .HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineSnapshot>(e =>
        {
            e.HasIndex(s => new { s.WorkspaceId, s.PipelineId, s.StageId });
            e.HasOne(s => s.Workspace).WithMany()
                .HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ForecastSnapshot>(e =>
        {
            e.HasIndex(s => new { s.WorkspaceId, s.PipelineId, s.ForecastCategory });
            e.HasOne(s => s.Workspace).WithMany()
                .HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ActivityMetric>(e =>
        {
            e.HasIndex(m => new { m.WorkspaceId, m.Date, m.Type });
            e.HasOne(m => m.Workspace).WithMany()
                .HasForeignKey(m => m.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Listing>(e =>
        {
            // One Listing per Deal — the optional-module data extends a
            // core Deal 1:1 rather than replacing it (workspace-customization
            // skill §4). Cascades with the Deal it extends.
            e.HasIndex(l => l.DealId).IsUnique();
            e.Property(l => l.CommissionPercent).HasPrecision(5, 2);
            e.HasOne(l => l.Workspace).WithMany()
                .HasForeignKey(l => l.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Deal).WithOne()
                .HasForeignKey<Listing>(l => l.DealId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.HashedKey).IsUnique();
            e.HasIndex(k => new { k.WorkspaceId, k.RevokedAt });
            e.HasOne(k => k.Workspace).WithMany()
                .HasForeignKey(k => k.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.HasIndex(s => new { s.WorkspaceId, s.IsActive });
            e.HasOne(s => s.Workspace).WithMany()
                .HasForeignKey(s => s.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasIndex(d => new { d.SubscriptionId, d.Status, d.CreatedAt });
            e.HasOne(d => d.Subscription).WithMany()
                .HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasIndex(r => new { r.WorkspaceId, r.Name }).IsUnique();
            e.HasOne(r => r.Workspace).WithMany()
                .HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
