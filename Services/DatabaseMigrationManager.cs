using System.Data;
using CandidatePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CandidatePortal.Api.Services;

public sealed class DatabaseMigrationManager(PortalDbContext database)
{
    public const string InitialBaselineMigration = "20260929184134_InitialBaseline";
    private const string EfProductVersion = "10.0.10";

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (await database.Database.CanConnectAsync(cancellationToken))
        {
            var applied = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (applied.Length == 0 && await HasPortalTablesAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The database already contains Candidate Portal tables but has no EF migration history. " +
                    "Back it up and run with --baseline-existing-database before --migrate.");
            }
        }

        await database.Database.MigrateAsync(cancellationToken);
    }

    public async Task<bool> BaselineExistingAsync(CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken) ||
            !await HasPortalTablesAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "No existing Candidate Portal schema was found. Use --migrate to create a new database.");
        }

        var applied = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (applied.Contains(InitialBaselineMigration, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        if (applied.Length > 0)
        {
            throw new InvalidOperationException(
                "The database contains migration history that does not include the expected initial baseline. " +
                "Review __EFMigrationsHistory manually before continuing.");
        }

        await ValidateCurrentSchemaAsync(cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__EFMigrationsHistory] (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END;

            IF NOT EXISTS (
                SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = {0}
            )
            BEGIN
                INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ({0}, {1});
            END;
            """,
            [InitialBaselineMigration, EfProductVersion],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The database is unavailable or has not been created. Run the application with --migrate first.");
        }

        var pending = (await database.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var applied = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (applied.Length == 0 && await HasPortalTablesAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The existing database has not been baselined for EF Core migrations. " +
                "Back it up and run with --baseline-existing-database.");
        }

        throw new InvalidOperationException(
            $"The database has pending EF Core migrations: {string.Join(", ", pending)}. " +
            "Run the application with --migrate before starting the API.");
    }

    private async Task<bool> HasPortalTablesAsync(CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'users', N'jobs', N'applications');";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return count > 0;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task ValidateCurrentSchemaAsync(CancellationToken cancellationToken)
    {
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actualIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = database.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT schemas.[name], tables.[name], columns.[name]
                    FROM sys.columns AS columns
                    INNER JOIN sys.tables AS tables ON tables.[object_id] = columns.[object_id]
                    INNER JOIN sys.schemas AS schemas ON schemas.[schema_id] = tables.[schema_id];
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    actualColumns.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}");
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT schemas.[name], tables.[name], indexes.[name]
                    FROM sys.indexes AS indexes
                    INNER JOIN sys.tables AS tables ON tables.[object_id] = indexes.[object_id]
                    INNER JOIN sys.schemas AS schemas ON schemas.[schema_id] = tables.[schema_id]
                    WHERE indexes.[name] IS NOT NULL AND indexes.[is_hypothetical] = 0;
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    actualIndexes.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}");
                }
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }

        var missing = new List<string>();
        foreach (var entityType in database.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            var configuredSchema = entityType.GetSchema();
            var schemaName = configuredSchema ?? database.Model.GetDefaultSchema() ?? "dbo";
            var storeObject = StoreObjectIdentifier.Table(tableName, configuredSchema);
            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (columnName is not null && !actualColumns.Contains($"{schemaName}.{tableName}.{columnName}"))
                {
                    missing.Add($"column {schemaName}.{tableName}.{columnName}");
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName(storeObject);
                if (indexName is not null && !actualIndexes.Contains($"{schemaName}.{tableName}.{indexName}"))
                {
                    missing.Add($"index {schemaName}.{tableName}.{indexName}");
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The existing database does not match the migration baseline. Missing: " +
                string.Join(", ", missing.Take(20)) +
                (missing.Count > 20 ? $" and {missing.Count - 20} more" : "") + ".");
        }
    }
}
