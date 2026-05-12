using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;

namespace Propel.Api.Gateway.Infrastructure.Database;

/// <summary>
/// Advanced migration management utility with history tracking and rollback support.
/// Provides detailed information about migration history and status.
/// </summary>
public sealed class MigrationManager
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<MigrationManager> _logger;

    public MigrationManager(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<MigrationManager> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets detailed migration history including applied and pending migrations.
    /// </summary>
    public async Task<MigrationHistory> GetMigrationHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        var history = new MigrationHistory
        {
            TotalMigrations = appliedMigrations.Count + pendingMigrations.Count,
            AppliedCount = appliedMigrations.Count,
            PendingCount = pendingMigrations.Count,
            LastAppliedMigration = appliedMigrations.LastOrDefault(),
            Migrations = BuildMigrationList(appliedMigrations, pendingMigrations)
        };

        return history;
    }

    /// <summary>
    /// Gets the current database schema version based on the last applied migration.
    /// </summary>
    public async Task<string> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var lastMigration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        return lastMigration ?? "No migrations applied";
    }

    /// <summary>
    /// Validates that all required database extensions are installed.
    /// </summary>
    public async Task<ExtensionValidationResult> ValidateExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var result = new ExtensionValidationResult();

        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);

            // Check for pgcrypto (required for encryption)
            result.PgcryptoInstalled = await CheckExtensionAsync(context, "pgcrypto", cancellationToken);

            // Check for vector (optional, for AI features)
            result.PgvectorInstalled = await CheckExtensionAsync(context, "vector", cancellationToken);

            result.AllRequiredInstalled = result.PgcryptoInstalled;
            result.AllOptionalInstalled = result.PgvectorInstalled;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate extensions");
            result.ValidationError = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets statistics about the database schema.
    /// </summary>
    public async Task<DatabaseStatistics> GetDatabaseStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var stats = new DatabaseStatistics();

        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);

            // Get table count
            using var tableCmd = context.Database.GetDbConnection().CreateCommand();
            tableCmd.CommandText = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE';";
            stats.TableCount = Convert.ToInt32(await tableCmd.ExecuteScalarAsync(cancellationToken));

            // Get view count
            using var viewCmd = context.Database.GetDbConnection().CreateCommand();
            viewCmd.CommandText = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = 'public' AND table_type = 'VIEW';";
            stats.ViewCount = Convert.ToInt32(await viewCmd.ExecuteScalarAsync(cancellationToken));

            // Get index count
            using var indexCmd = context.Database.GetDbConnection().CreateCommand();
            indexCmd.CommandText = @"
                SELECT COUNT(*) 
                FROM pg_indexes 
                WHERE schemaname = 'public';";
            stats.IndexCount = Convert.ToInt32(await indexCmd.ExecuteScalarAsync(cancellationToken));

            // Get database size
            using var sizeCmd = context.Database.GetDbConnection().CreateCommand();
            sizeCmd.CommandText = "SELECT pg_database_size(current_database());";
            var sizeBytes = Convert.ToInt64(await sizeCmd.ExecuteScalarAsync(cancellationToken));
            stats.DatabaseSizeBytes = sizeBytes;
            stats.DatabaseSizeMB = Math.Round(sizeBytes / (1024.0 * 1024.0), 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get database statistics");
        }

        return stats;
    }

    private async Task<bool> CheckExtensionAsync(AppDbContext context, string extensionName, CancellationToken cancellationToken)
    {
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname = '{extensionName}');";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool exists && exists;
        }
        catch
        {
            return false;
        }
    }

    private List<MigrationInfo> BuildMigrationList(List<string> applied, List<string> pending)
    {
        var migrations = new List<MigrationInfo>();

        foreach (var migration in applied)
        {
            migrations.Add(new MigrationInfo
            {
                MigrationId = migration,
                Status = MigrationStatus.Applied,
                DisplayName = FormatMigrationName(migration),
                AppliedDate = ExtractDateFromMigrationId(migration)
            });
        }

        foreach (var migration in pending)
        {
            migrations.Add(new MigrationInfo
            {
                MigrationId = migration,
                Status = MigrationStatus.Pending,
                DisplayName = FormatMigrationName(migration)
            });
        }

        return migrations.OrderBy(m => m.MigrationId).ToList();
    }

    private string FormatMigrationName(string migrationId)
    {
        // Extract readable name from migration ID (e.g., "20260420161639_Initial" -> "Initial")
        var parts = migrationId.Split('_', 2);
        return parts.Length > 1 ? parts[1] : migrationId;
    }

    private DateTime? ExtractDateFromMigrationId(string migrationId)
    {
        // Migration IDs start with timestamp: YYYYMMDDHHmmss
        if (migrationId.Length >= 14 && DateTime.TryParseExact(
            migrationId[..14],
            "yyyyMMddHHmmss",
            null,
            System.Globalization.DateTimeStyles.None,
            out var date))
        {
            return date;
        }
        return null;
    }
}

public sealed class MigrationHistory
{
    public int TotalMigrations { get; init; }
    public int AppliedCount { get; init; }
    public int PendingCount { get; init; }
    public string? LastAppliedMigration { get; init; }
    public List<MigrationInfo> Migrations { get; init; } = new();
}

public sealed class MigrationInfo
{
    public string MigrationId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public MigrationStatus Status { get; init; }
    public DateTime? AppliedDate { get; init; }
}

public enum MigrationStatus
{
    Applied,
    Pending
}

public sealed class ExtensionValidationResult
{
    public bool PgcryptoInstalled { get; set; }
    public bool PgvectorInstalled { get; set; }
    public bool AllRequiredInstalled { get; set; }
    public bool AllOptionalInstalled { get; set; }
    public string? ValidationError { get; set; }

    public List<string> GetMissingExtensions()
    {
        var missing = new List<string>();
        if (!PgcryptoInstalled) missing.Add("pgcrypto (required for PHI encryption)");
        if (!PgvectorInstalled) missing.Add("pgvector (optional for AI embeddings)");
        return missing;
    }
}

public sealed class DatabaseStatistics
{
    public int TableCount { get; set; }
    public int ViewCount { get; set; }
    public int IndexCount { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public double DatabaseSizeMB { get; set; }
}
