using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CandidatePortal.Api.Controllers;

[ApiController, Route("api/auth/external")]
public sealed class ExternalAuthController(
    PortalDbContext database,
    PortalOptions options,
    PasswordHasher passwordHasher,
    PortalSignInService signInService,
    SharePointOutboxService sharePointOutbox,
    ILogger<ExternalAuthController> logger) : ControllerBase
{
    private const int ExchangeCodeMinutes = 5;

    [AllowAnonymous, HttpGet("providers")]
    public ExternalAuthProvidersResponse Providers() =>
        new(options.GoogleAuthEnabled, options.MicrosoftAuthEnabled);

    [AllowAnonymous, HttpGet("{provider}")]
    public ActionResult Start(string provider, [FromQuery(Name = "return_url")] string? returnUrl)
    {
        var providerName = NormalizeProvider(provider);
        var scheme = ProviderScheme(providerName);
        var callbackUrl = ValidateReturnUrl(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback)),
        };
        properties.Items["provider"] = providerName;
        properties.Items["return_url"] = callbackUrl;
        return Challenge(properties, scheme);
    }

    [AllowAnonymous, HttpGet("callback")]
    public async Task<IActionResult> Callback(CancellationToken cancellationToken)
    {
        var authentication = await HttpContext.AuthenticateAsync(ExternalAuthSchemes.ExternalCookie);
        var returnUrl = ValidateReturnUrl(PropertyItem(authentication.Properties, "return_url"));
        await HttpContext.SignOutAsync(ExternalAuthSchemes.ExternalCookie);

        if (!authentication.Succeeded || authentication.Principal is null)
        {
            return RedirectWithError(returnUrl, "External sign-in could not be completed. Please try again.");
        }

        try
        {
            var provider = NormalizeProvider(PropertyItem(authentication.Properties, "provider") ?? "");
            var providerUserId = Claim(authentication.Principal, ClaimTypes.NameIdentifier, "sub", "id");
            var email = Claim(authentication.Principal, ClaimTypes.Email, "email", "preferred_username")
                .Trim().ToLowerInvariant();
            if (providerUserId.Length == 0 || email.Length == 0)
            {
                return RedirectWithError(returnUrl, "Your provider did not return a verified email address.");
            }

            var user = await FindOrCreateCandidateAsync(
                provider, providerUserId, email, authentication.Principal, cancellationToken);
            var exchangeCode = await CreateExchangeCodeAsync(user.Id, cancellationToken);
            return Redirect(QueryHelpers.AddQueryString(returnUrl, "code", exchangeCode));
        }
        catch (ApiException error)
        {
            return RedirectWithError(returnUrl, error.Detail);
        }
        catch (Exception error)
        {
            logger.LogError(error, "External candidate sign-in failed");
            return RedirectWithError(returnUrl, "External sign-in could not be completed. Please try again.");
        }
    }

    [AllowAnonymous, HttpPost("exchange")]
    public async Task<ActionResult<AuthResponse>> Exchange(
        ExternalAuthExchangeRequest payload, CancellationToken cancellationToken)
    {
        var codeHash = HashCode(payload.Code);
        var now = PortalClock.UtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var code = await database.ExternalAuthCodes.AsNoTracking().Include(value => value.User)
            .SingleOrDefaultAsync(value => value.CodeHash == codeHash, cancellationToken)
            ?? throw new ApiException(400, "The sign-in code is invalid or has already been used");
        if (code.ExpiresAt <= now)
        {
            await database.ExternalAuthCodes.Where(value => value.CodeHash == codeHash)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ApiException(400, "The sign-in code has expired. Please sign in again");
        }

        var deleted = await database.ExternalAuthCodes
            .Where(value => value.CodeHash == codeHash && value.ExpiresAt > now)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted != 1)
        {
            throw new ApiException(400, "The sign-in code is invalid or has already been used");
        }
        var response = await signInService.IssueAsync(code.User, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task<User> FindOrCreateCandidateAsync(
        string provider,
        string providerUserId,
        string email,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var linked = await database.ExternalLogins.Include(value => value.User).SingleOrDefaultAsync(
            value => value.Provider == provider && value.ProviderUserId == providerUserId,
            cancellationToken);
        if (linked is not null)
        {
            EnsureCandidate(linked.User);
            var linkedBecameVerified = !linked.User.IsEmailVerified;
            linked.User.IsEmailVerified = true;
            if (linked.Email != email)
            {
                linked.Email = email;
            }
            if (linkedBecameVerified)
                sharePointOutbox.EnqueueCandidate(linked.User.Id);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return linked.User;
        }

        var user = await database.Users.SingleOrDefaultAsync(value => value.Email == email, cancellationToken);
        bool created;
        bool becameVerified;
        if (user is null)
        {
            created = true;
            becameVerified = false;
            var (firstName, lastName) = CandidateName(principal, email);
            user = new User
            {
                Email = email,
                PasswordHash = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
                FirstName = firstName,
                LastName = lastName,
                Phone = "",
                Title = "Candidate",
                About = "",
                Role = PortalRoles.Candidate,
                Preferences = new UserPreference(),
            };
            database.Users.Add(user);
        }
        else
        {
            created = false;
            EnsureCandidate(user);
            becameVerified = !user.IsEmailVerified;
            user.IsEmailVerified = true;
        }

        database.ExternalLogins.Add(new ExternalLogin
        {
            User = user,
            Provider = provider,
            ProviderUserId = providerUserId,
            Email = email,
        });
        await database.SaveChangesAsync(cancellationToken);
        if (created || becameVerified)
        {
            sharePointOutbox.EnqueueCandidate(user.Id);
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    private async Task<string> CreateExchangeCodeAsync(int userId, CancellationToken cancellationToken)
    {
        var now = PortalClock.UtcNow();
        await database.ExternalAuthCodes.Where(value => value.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
        var rawCode = PasswordHasher.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        database.ExternalAuthCodes.Add(new ExternalAuthCode
        {
            CodeHash = HashCode(rawCode),
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ExchangeCodeMinutes),
        });
        await database.SaveChangesAsync(cancellationToken);
        return rawCode;
    }

    private string ValidateReturnUrl(string? returnUrl)
    {
        var allowedOrigins = options.FrontendOrigins
            .Select(value => value.Trim().TrimEnd('/'))
            .Where(value => value.Length > 0)
            .ToArray();
        var defaultOrigin = allowedOrigins.FirstOrDefault() ?? "http://localhost:5174";
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return $"{defaultOrigin}/auth/callback";
        }
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ApiException(400, "The external sign-in return URL is invalid");
        }
        var requestedOrigin = $"{uri.Scheme}://{uri.Authority}".TrimEnd('/');
        if (!allowedOrigins.Contains(requestedOrigin, StringComparer.OrdinalIgnoreCase))
        {
            throw new ApiException(400, "The external sign-in return URL is not allowed");
        }
        return $"{requestedOrigin}/auth/callback";
    }

    private string ProviderScheme(string provider) => provider switch
    {
        "google" when options.GoogleAuthEnabled => ExternalAuthSchemes.Google,
        "microsoft" when options.MicrosoftAuthEnabled => ExternalAuthSchemes.Microsoft,
        _ => throw new ApiException(404, $"{provider} sign-in is not configured"),
    };

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "google" => "google",
        "microsoft" => "microsoft",
        _ => throw new ApiException(404, "External sign-in provider not found"),
    };

    private static string Claim(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes.Select(value => principal.FindFirstValue(value)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string? PropertyItem(AuthenticationProperties? properties, string key) =>
        properties is not null && properties.Items.TryGetValue(key, out var value) ? value : null;

    private static (string FirstName, string LastName) CandidateName(ClaimsPrincipal principal, string email)
    {
        var firstName = Claim(principal, ClaimTypes.GivenName, "given_name").Trim();
        var lastName = Claim(principal, ClaimTypes.Surname, "family_name").Trim();
        if (firstName.Length == 0)
        {
            var displayName = Claim(principal, ClaimTypes.Name, "name").Trim();
            var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            firstName = parts.FirstOrDefault() ?? email.Split('@')[0];
            lastName = parts.Length > 1 ? parts[1] : "";
        }
        return (firstName[..Math.Min(firstName.Length, 100)], lastName[..Math.Min(lastName.Length, 100)]);
    }

    private static void EnsureCandidate(User user)
    {
        if (user.Role != PortalRoles.Candidate)
        {
            throw new ApiException(403, "Non-candidate accounts must use password sign-in");
        }
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    private static RedirectResult RedirectWithError(string returnUrl, string detail) =>
        new(QueryHelpers.AddQueryString(returnUrl, "error", detail));
}
