using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Security;

public sealed record PortalTokenClaims(int UserId, string Role, string SessionId, DateTime ExpiresAt);

public sealed class TokenService(PortalOptions options)
{
    public DateTime AccessTokenExpiry() => PortalClock.UtcNow().AddMinutes(options.AccessTokenMinutes);

    public string Create(int userId, string role, string sessionId, DateTime expiresAt)
    {
        var header = PasswordHasher.Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var unixExpiry = new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var payload = PasswordHasher.Base64UrlEncode(
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                sub = userId.ToString(),
                role,
                jti = sessionId,
                exp = unixExpiry,
            }));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey));
        var signature = PasswordHasher.Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    public PortalTokenClaims? Decode(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey));
            var expected = hmac.ComputeHash(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            var actual = PasswordHasher.Base64UrlDecode(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return null;
            }

            using var payload = JsonDocument.Parse(PasswordHasher.Base64UrlDecode(parts[1]));
            var root = payload.RootElement;
            var userId = int.Parse(root.GetProperty("sub").GetString()!);
            var role = root.GetProperty("role").GetString()!;
            var sessionId = root.GetProperty("jti").GetString()!;
            var expiry = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64()).UtcDateTime;
            if (expiry < DateTime.UtcNow)
            {
                return null;
            }
            return new PortalTokenClaims(userId, role, sessionId, DateTime.SpecifyKind(expiry, DateTimeKind.Unspecified));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
