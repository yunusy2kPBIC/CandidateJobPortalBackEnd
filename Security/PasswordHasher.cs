using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;

namespace CandidatePortal.Api.Security;

public sealed class PasswordHasher
{
    private const int Cost = 1 << 14;
    private const int BlockSize = 8;
    private const int Parallelization = 1;
    private const int DerivedLength = 64;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var digest = SCrypt.Generate(Encoding.UTF8.GetBytes(password), salt, Cost, BlockSize, Parallelization, DerivedLength);
        return $"scrypt${Base64UrlEncode(salt)}${Base64UrlEncode(digest)}";
    }

    public bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$', 3);
            if (parts.Length != 3 || parts[0] != "scrypt")
            {
                return false;
            }

            var salt = Base64UrlDecode(parts[1]);
            var expected = Base64UrlDecode(parts[2]);
            var actual = SCrypt.Generate(
                Encoding.UTF8.GetBytes(password), salt, Cost, BlockSize, Parallelization, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception) when (encoded is not null)
        {
            return false;
        }
    }

    internal static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }
}
