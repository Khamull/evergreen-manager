namespace Evergreen.Domain;

public interface ITenantOwned { Guid TenantId { get; set; } }

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
}

public sealed class AppUser : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = "Admin";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Article : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string SourceKey { get; set; }
    public required string Url { get; set; }
    public required string Title { get; set; }
    public string? Slug { get; set; }
    public string? Excerpt { get; set; }
    public string? ContentHtml { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorUrl { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? FeaturedImageUrl { get; set; }
    public string? CanonicalUrl { get; set; }
    public string[] Tags { get; set; } = [];
    public string[] Categories { get; set; } = [];
    public string? SeoDescription { get; set; }
    public string? MetaTitle { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Campaign : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Draft";
    public string SelectionJson { get; set; } = "{}";
    public string ScheduleJson { get; set; } = "{}";
    public string Instructions { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromptProfile : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Network { get; set; }
    public string BasePrompt { get; set; } = "";
    public string NetworkInstructions { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScrapeRun : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string SourceUrl { get; set; }
    public string Status { get; set; } = "Running";
    public int Discovered { get; set; }
    public int Imported { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}
