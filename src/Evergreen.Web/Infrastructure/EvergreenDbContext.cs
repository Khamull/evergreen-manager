using Evergreen.Domain;
using Microsoft.EntityFrameworkCore;

namespace Evergreen.Infrastructure;

public sealed class EvergreenDbContext(DbContextOptions<EvergreenDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<PromptProfile> PromptProfiles => Set<PromptProfile>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();
    public Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        model.Entity<AppUser>().HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        model.Entity<Article>().HasIndex(x => new { x.TenantId, x.SourceKey }).IsUnique();
        model.Entity<Campaign>().HasIndex(x => new { x.TenantId, x.Name });
        model.Entity<PromptProfile>().HasIndex(x => new { x.TenantId, x.Network, x.Name });
        model.Entity<Article>().Property(x => x.Tags).HasColumnType("text[]");
        model.Entity<Article>().Property(x => x.Categories).HasColumnType("text[]");
        model.Entity<AppUser>().HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        model.Entity<Article>().HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        model.Entity<Campaign>().HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        model.Entity<PromptProfile>().HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        model.Entity<ScrapeRun>().HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
    }
}
