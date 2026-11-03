using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Evergreen.Application;
using Evergreen.Domain;
using Evergreen.Infrastructure;
using Evergreen.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<EvergreenDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddHttpClient<IArticleCrawler, WordPressArticleCrawler>(c => { c.Timeout = TimeSpan.FromSeconds(30); c.DefaultRequestHeaders.UserAgent.ParseAdd("EvergreenContentManager/1.0 (+local content discovery)"); });
builder.Services.AddScoped<ArticleImportService>();
builder.Services.AddSingleton<ICampaignSchedulePlanner, CampaignSchedulePlanner>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().WithOrigins("http://localhost:5080", "https://localhost:7080")));
var jwtKey = builder.Configuration["Jwt:Key"] ?? "LOCAL-ONLY-CHANGE-THIS-KEY-AT-LEAST-32-CHARS";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = false, ValidateAudience = false, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30) }; });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<EvergreenDbContext>();
    await db.Database.EnsureCreatedAsync(); // convenient local bootstrap; replace with migrations for shared environments.
}
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Evergreen", version = "0.1.0" }));

app.MapPost("/api/auth/register", async (RegisterRequest req, EvergreenDbContext db, IPasswordHasher<AppUser> hasher) => {
    if (string.IsNullOrWhiteSpace(req.CompanyName) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 12) return Results.BadRequest(new { error = "Company, email and a password of at least 12 characters are required." });
    var slug = Slugify(req.CompanyName);
    if (await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug)) return Results.Conflict(new { error = "A company with this slug already exists." });
    var tenant = new Tenant { Name = req.CompanyName.Trim(), Slug = slug };
    var user = new AppUser { TenantId = tenant.Id, Email = req.Email.Trim().ToLowerInvariant(), PasswordHash = "pending" };
    user.PasswordHash = hasher.HashPassword(user, req.Password);
    db.Tenants.Add(tenant); db.Users.Add(user); await db.SaveChangesAsync();
    return Results.Created("/api/auth/login", new { tenant.Id, tenant.Name, tenant.Slug, user.Email });
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (LoginRequest req, EvergreenDbContext db, IPasswordHasher<AppUser> hasher, IConfiguration config) => {
    var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Email == req.Email.Trim().ToLowerInvariant() && db.Tenants.IgnoreQueryFilters().Any(t => t.Id == x.TenantId && t.Slug == req.TenantSlug));
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    var tenant = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == user.TenantId && x.IsActive);
    if (tenant is null) return Results.Unauthorized();
    var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, user.Role), new Claim("tenant_id", user.TenantId.ToString()), new Claim("tenant_slug", tenant.Slug) };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? "LOCAL-ONLY-CHANGE-THIS-KEY-AT-LEAST-32-CHARS"));
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(8), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), expiresIn = 28800, tenant = tenant.Name });
}).AllowAnonymous();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", (HttpContext ctx) => Results.Ok(new { user = ctx.User.FindFirstValue(ClaimTypes.Email), tenantId = ctx.User.FindFirstValue("tenant_id"), tenant = ctx.User.FindFirstValue("tenant_slug") }));
api.MapGet("/articles/{id:guid}", async (Guid id, EvergreenDbContext db) => { var article = await db.Articles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); return article is null ? Results.NotFound() : Results.Ok(article); });
api.MapGet("/articles", async (EvergreenDbContext db, string? q, string? author, string? category, int page = 1, int pageSize = 50) => {
    page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
    var query = db.Articles.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Title.Contains(q) || (x.Excerpt != null && x.Excerpt.Contains(q)));
    if (!string.IsNullOrWhiteSpace(author)) query = query.Where(x => x.AuthorName == author);
    if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Categories.Contains(category));
    var total = await query.CountAsync(); var items = await query.OrderByDescending(x => x.PublishedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    return Results.Ok(new { total, page, pageSize, items });
});
api.MapGet("/articles/facets", async (EvergreenDbContext db) => Results.Ok(new { authors = await db.Articles.Select(x => x.AuthorName).Where(x => x != null && x != "").Distinct().OrderBy(x => x).ToListAsync(), categories = await db.Articles.SelectMany(x => x.Categories).Distinct().OrderBy(x => x).ToListAsync() }));
api.MapPost("/sources/wordpress/import", async (ImportRequest req, HttpContext ctx, ArticleImportService importer, EvergreenDbContext db) => {
    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return Results.BadRequest(new { error = "A valid HTTP(S) site URL is required." });
    var tenantId = Guid.Parse(ctx.User.FindFirstValue("tenant_id")!);
    var run = new ScrapeRun { TenantId = tenantId, SourceUrl = uri.ToString() }; db.ScrapeRuns.Add(run); await db.SaveChangesAsync();
    try { var result = await importer.ImportAsync(uri, tenantId, ctx.RequestAborted); run.Status = "Completed"; run.Discovered = result.Discovered; run.Imported = result.Imported; run.FinishedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return Results.Ok(new { run.Id, result.Discovered, result.Imported, run.Status }); }
    catch (Exception ex) { run.Status = "Failed"; run.Error = ex.Message; run.FinishedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); throw; }
});
api.MapGet("/scrape-runs", async (EvergreenDbContext db) => Results.Ok(await db.ScrapeRuns.OrderByDescending(x => x.StartedAt).Take(50).ToListAsync()));
api.MapPost("/campaigns/preview-schedule", (SchedulePreviewRequest req, ICampaignSchedulePlanner planner) =>
{
    if (!TimeZoneInfo.TryFindSystemTimeZoneById(req.TimeZone, out var zone)) return Results.BadRequest(new { error = "Unknown timezone ID." });
    var slots = planner.Build(new ScheduleRequest(req.StartDate, req.Days, req.PostsPerDay, req.Times, req.AllowedDays), zone);
    return Results.Ok(new { count = slots.Count, slots });
});
api.MapGet("/campaigns", async (EvergreenDbContext db) => Results.Ok(await db.Campaigns.OrderByDescending(x => x.CreatedAt).ToListAsync()));
api.MapPost("/campaigns", async (CampaignRequest req, EvergreenDbContext db, HttpContext ctx) => {
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Campaign name is required." });
    var campaign = new Campaign { TenantId = Guid.Parse(ctx.User.FindFirstValue("tenant_id")!), Name = req.Name.Trim(), Description = req.Description, SelectionJson = req.SelectionJson ?? "{}", ScheduleJson = req.ScheduleJson ?? "{}", Instructions = req.Instructions ?? "" };
    db.Campaigns.Add(campaign); await db.SaveChangesAsync(); return Results.Created($"/api/campaigns/{campaign.Id}", campaign);
});
api.MapGet("/prompts", async (EvergreenDbContext db) => Results.Ok(await db.PromptProfiles.OrderBy(x => x.Network).ThenBy(x => x.Name).ToListAsync()));
api.MapPost("/prompts", async (PromptRequest req, EvergreenDbContext db, HttpContext ctx) => {
    var prompt = new PromptProfile { TenantId = Guid.Parse(ctx.User.FindFirstValue("tenant_id")!), Name = req.Name, Network = req.Network, BasePrompt = req.BasePrompt ?? "", NetworkInstructions = req.NetworkInstructions ?? "" };
    db.PromptProfiles.Add(prompt); await db.SaveChangesAsync(); return Results.Created($"/api/prompts/{prompt.Id}", prompt);
});

app.Run();
static string Slugify(string value)
{
    var normalized = value.Normalize(NormalizationForm.FormD);
    var withoutMarks = new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    var slug = string.Concat(withoutMarks.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
    while (slug.Contains("--")) slug = slug.Replace("--", "-");
    return slug.Trim('-');
}
public sealed record RegisterRequest(string CompanyName, string Email, string Password);
public sealed record LoginRequest(string TenantSlug, string Email, string Password);
public sealed record SchedulePreviewRequest(DateOnly StartDate, int Days, int PostsPerDay, TimeOnly[] Times, string TimeZone = "Europe/Lisbon", DayOfWeek[]? AllowedDays = null);
public sealed record ImportRequest(string Url);
public sealed record CampaignRequest(string Name, string? Description, string? SelectionJson, string? ScheduleJson, string? Instructions);
public sealed record PromptRequest(string Name, string Network, string? BasePrompt, string? NetworkInstructions);
public partial class Program { }
