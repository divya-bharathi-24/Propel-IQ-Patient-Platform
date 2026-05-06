using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;

namespace Propel.Api.Gateway.Infrastructure.Database;

/// <summary>
/// Utility to fix the missing pending_alerts_json column issue.
/// This column was defined in migration 20260422140000_AddPatientPendingAlerts but was not applied.
/// </summary>
public sealed class DatabaseColumnFixer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DatabaseColumnFixer> _logger;

    public DatabaseColumnFixer(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DatabaseColumnFixer> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Fixes the missing pending_alerts_json column and updates migration history.
    /// </summary>
    public async Task<FixResult> FixMissingPendingAlertsColumnAsync(CancellationToken cancellationToken = default)
    {
        var result = new FixResult();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.OpenConnectionAsync(cancellationToken);

            // Step 1: Check if column exists
            _logger.LogInformation("Checking if pending_alerts_json column exists...");
            var columnExists = await CheckColumnExistsAsync(context, cancellationToken);
            result.ColumnExistedBefore = columnExists;

            if (!columnExists)
            {
                _logger.LogInformation("Column missing. Adding pending_alerts_json column...");
                await AddColumnAsync(context, cancellationToken);
                result.ColumnAdded = true;
                _logger.LogInformation("✓ Column added successfully");
            }
            else
            {
                _logger.LogInformation("✓ Column already exists");
            }

            // Step 2: Check if migration record exists
            _logger.LogInformation("Checking migration history...");
            var migrationExists = await CheckMigrationExistsAsync(context, cancellationToken);
            result.MigrationExistedBefore = migrationExists;

            if (!migrationExists)
            {
                _logger.LogInformation("Migration record missing. Adding to history...");
                await AddMigrationRecordAsync(context, cancellationToken);
                result.MigrationRecordAdded = true;
                _logger.LogInformation("✓ Migration record added successfully");
            }
            else
            {
                _logger.LogInformation("✓ Migration record already exists");
            }

            result.Success = true;
            result.Message = "Database schema is now in sync with codebase";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix missing column");
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
            result.ErrorDetails = ex.ToString();
        }

        return result;
    }

    private async Task<bool> CheckColumnExistsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT EXISTS (
                SELECT 1 
                FROM information_schema.columns 
                WHERE table_name = 'patients' 
                  AND column_name = 'pending_alerts_json'
            );";

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private async Task AddColumnAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var sql = "ALTER TABLE patients ADD COLUMN pending_alerts_json jsonb NULL;";
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task<bool> CheckMigrationExistsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT EXISTS (
                SELECT 1 
                FROM ""__EFMigrationsHistory"" 
                WHERE migration_id = '20260422140000_AddPatientPendingAlerts'
            );";

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    private async Task AddMigrationRecordAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var sql = @"
            INSERT INTO ""__EFMigrationsHistory"" (migration_id, product_version)
            VALUES ('20260422140000_AddPatientPendingAlerts', '9.0.15');";

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}

public sealed class FixResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ColumnExistedBefore { get; set; }
    public bool ColumnAdded { get; set; }
    public bool MigrationExistedBefore { get; set; }
    public bool MigrationRecordAdded { get; set; }
    public string? ErrorDetails { get; set; }
}
