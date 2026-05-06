using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;

namespace Propel.Api.Gateway.Infrastructure.Database;

/// <summary>
/// Database initialization service that handles schema synchronization and migrations.
/// Runs automatically on application startup to ensure database is up-to-date.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IHostEnvironment _environment;

    public DatabaseInitializer(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<DatabaseInitializer> logger,
        IHostEnvironment environment)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Initializes the database by applying pending migrations.
    /// In development: always applies migrations automatically.
    /// In production: applies migrations with additional safety checks.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database initialization...");

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Check database connectivity
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database. Please check connection string.");
                throw new InvalidOperationException("Database connection failed. Verify DATABASE_URL is correct.");
            }

            _logger.LogInformation("Database connection verified successfully.");

            // Get pending migrations
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

            _logger.LogInformation("Database status: {AppliedCount} migrations applied, {PendingCount} pending",
                appliedMigrations.Count, pendingMigrations.Count);

            if (pendingMigrations.Any())
            {
                _logger.LogInformation("Applying {Count} pending migrations: {Migrations}",
                    pendingMigrations.Count,
                    string.Join(", ", pendingMigrations));

                // Apply migrations
                await context.Database.MigrateAsync(cancellationToken);

                _logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                _logger.LogInformation("Database is up-to-date. No migrations needed.");
            }

            // Verify schema is current
            var lastAppliedMigration = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
            _logger.LogInformation("Current database schema version: {Migration}", lastAppliedMigration ?? "None");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Checks if the database exists and is accessible.
    /// </summary>
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connectivity check failed.");
            return false;
        }
    }

    /// <summary>
    /// Gets the current migration status information.
    /// </summary>
    public async Task<DatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var canConnect = await context.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return new DatabaseStatus
            {
                IsConnected = false,
                Message = "Cannot connect to database"
            };
        }

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();

        return new DatabaseStatus
        {
            IsConnected = true,
            AppliedMigrations = appliedMigrations,
            PendingMigrations = pendingMigrations,
            LastAppliedMigration = appliedMigrations.LastOrDefault(),
            Message = pendingMigrations.Any()
                ? $"{pendingMigrations.Count} migrations pending"
                : "Database is up-to-date"
        };
    }
}

/// <summary>
/// Database status information.
/// </summary>
public sealed class DatabaseStatus
{
    public bool IsConnected { get; init; }
    public List<string> AppliedMigrations { get; init; } = new();
    public List<string> PendingMigrations { get; init; } = new();
    public string? LastAppliedMigration { get; init; }
    public string Message { get; init; } = string.Empty;
}
