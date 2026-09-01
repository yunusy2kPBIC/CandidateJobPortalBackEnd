using Npgsql;

namespace CandidatePortal.Api.Data;

public static class DatabaseUrl
{
    public static string ToNpgsqlConnectionString(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            return value;
        }

        var normalized = value
            .Replace("postgresql+psycopg://", "postgresql://", StringComparison.OrdinalIgnoreCase)
            .Replace("postgres+psycopg://", "postgresql://", StringComparison.OrdinalIgnoreCase);
        var uri = new Uri(normalized);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        };

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue("sslmode", out var sslMode) &&
            Enum.TryParse<SslMode>(sslMode.ToString(), true, out var parsedSslMode))
        {
            builder.SslMode = parsedSslMode;
        }
        return builder.ConnectionString;
    }
}
