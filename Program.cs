using System.Text.Json;
using System.Threading.RateLimiting;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var migrationArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--migrate",
    "--migrate-sharepoint",
    "--baseline-existing-database",
    "--seed-only",
};
var migrateOnly = args.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
var migrateSharePointOnly = args.Contains("--migrate-sharepoint", StringComparer.OrdinalIgnoreCase);
var baselineOnly = args.Contains("--baseline-existing-database", StringComparer.OrdinalIgnoreCase);
var seedOnly = args.Contains("--seed-only", StringComparer.OrdinalIgnoreCase);
if (new[] { migrateOnly, migrateSharePointOnly, baselineOnly, seedOnly }.Count(value => value) > 1)
{
    throw new InvalidOperationException(
        "Use only one maintenance command at a time: --migrate, --migrate-sharepoint, " +
        "--baseline-existing-database, or --seed-only.");
}
var hostArguments = args.Where(value => !migrationArguments.Contains(value)).ToArray();
var builder = WebApplication.CreateBuilder(hostArguments);

var repositoryRoot = Path.GetFullPath("..", builder.Environment.ContentRootPath);
builder.Configuration.AddDotEnvFiles(
    Path.Combine(repositoryRoot, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, ".env"));

var portalOptions = PortalOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(portalOptions);
builder.Services.AddDbContext<PortalDbContext>(options =>
    options.UseSqlServer(portalOptions.SqlServerConnectionString));

builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddScoped<DatabaseBootstrapper>();
builder.Services.AddScoped<DatabaseMigrationManager>();
builder.Services.AddScoped<PortalSignInService>();
builder.Services.AddScoped<SharePointSyncService>();
builder.Services.AddScoped<SharePointOutboxService>();
builder.Services.AddHostedService<SharePointOutboxWorker>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<MasterDataService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<VerificationEmailService>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddSingleton<PrivacyNoticeService>();
builder.Services.AddScoped<CooperativeTrainingSubmissionService>();
builder.Services.AddHttpClient<ISharePointClient, GraphSharePointClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(portalOptions.SharePointTimeoutSeconds));

builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        null);
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("email-availability", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("email-verification", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("password-recovery", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(portalOptions.FrontendOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var detail = string.Join("; ", context.ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "The request payload is invalid"
                    : error.ErrorMessage));
            return new UnprocessableEntityObjectResult(new { detail });
        };
    });
builder.Services.AddOpenApi();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Candidate Portal API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", service = portalOptions.AppName }))
    .AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var migrations = scope.ServiceProvider.GetRequiredService<DatabaseMigrationManager>();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    if (migrateSharePointOnly)
    {
        var result = await scope.ServiceProvider.GetRequiredService<ISharePointClient>().ProvisionAsync();
        Console.WriteLine(
            $"SharePoint schema migration completed successfully. Current version: {result.SchemaVersion}.");
        return;
    }
    if (migrateOnly)
    {
        await migrations.ApplyAsync();
        Console.WriteLine("Database migrations applied successfully.");
        return;
    }
    if (baselineOnly)
    {
        await bootstrapper.PrepareExistingSchemaForMigrationBaselineAsync();
        var created = await migrations.BaselineExistingAsync();
        Console.WriteLine(created
            ? "Existing database validated and migration baseline recorded successfully."
            : "The migration baseline is already recorded.");
        return;
    }

    await migrations.EnsureReadyAsync();
    await bootstrapper.InitializeAsync();
}

if (seedOnly)
{
    return;
}

await app.RunAsync();

public partial class Program;
