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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<WorkspaceMember>(e =>
        {
            e.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
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
    }
}
