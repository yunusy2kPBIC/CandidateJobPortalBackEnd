using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var seedOnly = args.Any(value => string.Equals(value, "--seed-only", StringComparison.OrdinalIgnoreCase));
var hostArguments = args.Where(value => !string.Equals(value, "--seed-only", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(hostArguments);

var repositoryRoot = Path.GetFullPath("..", builder.Environment.ContentRootPath);
builder.Configuration.AddDotEnvFiles(
    Path.Combine(repositoryRoot, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, ".env"));

var portalOptions = PortalOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(portalOptions);
builder.Services.AddDbContext<PortalDbContext>(options =>
{
    switch (portalOptions.DatabaseProvider)
    {
        case "sqlserver":
            //options.UseSqlServer(portalOptions.DatabaseUrl, sqlServer => sqlServer.EnableRetryOnFailure());
            options.UseSqlServer(portalOptions.DatabaseUrl);
            break;
        case "postgresql":
        case "postgres":
            options.UseNpgsql(DatabaseUrl.ToNpgsqlConnectionString(portalOptions.DatabaseUrl));
            break;
        default:
            throw new InvalidOperationException(
                $"Unsupported DATABASE_PROVIDER '{portalOptions.DatabaseProvider}'. Use sqlserver or postgresql.");
    }
});

builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddScoped<DatabaseBootstrapper>();
builder.Services.AddScoped<SharePointSyncService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddHttpClient<ISharePointClient, GraphSharePointClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(portalOptions.SharePointTimeoutSeconds));

const string authenticationScheme = "PortalSession";
builder.Services
    .AddAuthentication(authenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(authenticationScheme, null);
builder.Services.AddAuthorization();
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", service = portalOptions.AppName }))
    .AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>().InitializeAsync();
}

if (seedOnly)
{
    return;
}

await app.RunAsync();

public partial class Program;
