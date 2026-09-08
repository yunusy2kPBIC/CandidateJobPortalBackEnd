using System.Text.Json;
using System.Threading.RateLimiting;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
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
    options.UseSqlServer(portalOptions.SqlServerConnectionString));

builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddScoped<DatabaseBootstrapper>();
builder.Services.AddScoped<PortalSignInService>();
builder.Services.AddScoped<SharePointSyncService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<MasterDataService>();
builder.Services.AddScoped<VerificationEmailService>();
builder.Services.AddScoped<CooperativeTrainingSubmissionService>();
builder.Services.AddHttpClient<ISharePointClient, GraphSharePointClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(portalOptions.SharePointTimeoutSeconds));

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = ExternalAuthSchemes.Portal;
        options.DefaultChallengeScheme = ExternalAuthSchemes.Portal;
    })
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(ExternalAuthSchemes.Portal, null)
    .AddCookie(ExternalAuthSchemes.ExternalCookie, options =>
    {
        options.Cookie.Name = "candidate_portal_external";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });
if (portalOptions.GoogleAuthEnabled)
{
    authentication.AddGoogle(ExternalAuthSchemes.Google, options =>
    {
        options.SignInScheme = ExternalAuthSchemes.ExternalCookie;
        options.ClientId = portalOptions.GoogleAuthClientId;
        options.ClientSecret = portalOptions.GoogleAuthClientSecret;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var returnUrl = context.Properties?.Items.TryGetValue("return_url", out var value) == true
                ? value
                : $"{portalOptions.FrontendOrigins.First()}/auth/callback";
            context.Response.Redirect(QueryHelpers.AddQueryString(
                returnUrl!, "error", "Google sign-in was cancelled or could not be completed."));
            return Task.CompletedTask;
        };
    });
}
if (portalOptions.MicrosoftAuthEnabled)
{
    authentication.AddMicrosoftAccount(ExternalAuthSchemes.Microsoft, options =>
    {
        options.SignInScheme = ExternalAuthSchemes.ExternalCookie;
        options.ClientId = portalOptions.MicrosoftAuthClientId;
        options.ClientSecret = portalOptions.MicrosoftAuthClientSecret;
        options.CallbackPath = "/signin-microsoft";
        options.SaveTokens = false;
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var returnUrl = context.Properties?.Items.TryGetValue("return_url", out var value) == true
                ? value
                : $"{portalOptions.FrontendOrigins.First()}/auth/callback";
            context.Response.Redirect(QueryHelpers.AddQueryString(
                returnUrl!, "error", "Microsoft sign-in was cancelled or could not be completed."));
            return Task.CompletedTask;
        };
    });
}
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
    await scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>().InitializeAsync();
}

if (seedOnly)
{
    return;
}

await app.RunAsync();

public partial class Program;
