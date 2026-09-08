using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Models;
using CandidatePortal.Api.Security;

namespace CandidatePortal.Api.Services;

public sealed class PortalSignInService(PortalDbContext database, TokenService tokenService)
{
    public async Task<AuthResponse> IssueAsync(User user, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var expiresAt = tokenService.AccessTokenExpiry();
        database.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = user.Id, ExpiresAt = expiresAt });
        await database.SaveChangesAsync(cancellationToken);
        return new AuthResponse(tokenService.Create(user.Id, user.Role, sessionId, expiresAt), "bearer", user.ToResponse());
    }
}
