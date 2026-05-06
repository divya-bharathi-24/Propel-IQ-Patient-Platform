using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Infrastructure.Database;

namespace Propel.Api.Gateway.Endpoints;

/// <summary>
/// Database management endpoints for checking migration status and triggering synchronization.
/// </summary>
public static class DatabaseEndpoints
{
    public static void MapDatabaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/database")
            .WithTags("Database Management");

        // GET /api/database/status - Check database migration status
        group.MapGet("/status", GetDatabaseStatus)
            .WithName("GetDatabaseStatus")
            .WithSummary("Get current database migration status")
            .Produces<DatabaseStatusResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // POST /api/database/migrate - Apply pending migrations
        group.MapPost("/migrate", ApplyMigrations)
            .WithName("ApplyMigrations")
            .WithSummary("Apply all pending database migrations")
            .Produces<MigrationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // GET /api/database/health - Simple connectivity check
        group.MapGet("/health", CheckDatabaseHealth)
            .WithName("CheckDatabaseHealth")
            .WithSummary("Check database connectivity")
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetDatabaseStatus(
        DatabaseInitializer initializer,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await initializer.GetStatusAsync(cancellationToken);

            var response = new DatabaseStatusResponse
            {
                IsConnected = status.IsConnected,
                Message = status.Message,
                AppliedMigrationsCount = status.AppliedMigrations.Count,
                PendingMigrationsCount = status.PendingMigrations.Count,
                LastAppliedMigration = status.LastAppliedMigration,
                AppliedMigrations = status.AppliedMigrations,
                PendingMigrations = status.PendingMigrations
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Database status check failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ApplyMigrations(
        DatabaseInitializer initializer,
        CancellationToken cancellationToken)
    {
        try
        {
            var statusBefore = await initializer.GetStatusAsync(cancellationToken);

            if (!statusBefore.IsConnected)
            {
                return Results.Problem(
                    title: "Database not connected",
                    detail: "Cannot apply migrations. Database is not accessible.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!statusBefore.PendingMigrations.Any())
            {
                return Results.Ok(new MigrationResponse
                {
                    Success = true,
                    Message = "No migrations to apply. Database is already up-to-date.",
                    MigrationsApplied = 0
                });
            }

            await initializer.InitializeAsync(cancellationToken);

            var statusAfter = await initializer.GetStatusAsync(cancellationToken);

            return Results.Ok(new MigrationResponse
            {
                Success = true,
                Message = $"Successfully applied {statusBefore.PendingMigrations.Count} migration(s).",
                MigrationsApplied = statusBefore.PendingMigrations.Count,
                AppliedMigrations = statusBefore.PendingMigrations
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Migration failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CheckDatabaseHealth(
        DatabaseInitializer initializer,
        CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await initializer.CanConnectAsync(cancellationToken);

            if (!canConnect)
            {
                return Results.Problem(
                    title: "Database not connected",
                    detail: "Cannot connect to database. Check connection string and ensure database server is running.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new HealthResponse
            {
                Status = "Healthy",
                Message = "Database is connected and accessible"
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Health check failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public sealed record DatabaseStatusResponse
{
    public bool IsConnected { get; init; }
    public string Message { get; init; } = string.Empty;
    public int AppliedMigrationsCount { get; init; }
    public int PendingMigrationsCount { get; init; }
    public string? LastAppliedMigration { get; init; }
    public List<string> AppliedMigrations { get; init; } = new();
    public List<string> PendingMigrations { get; init; } = new();
}

public sealed record MigrationResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int MigrationsApplied { get; init; }
    public List<string> AppliedMigrations { get; init; } = new();
}

public sealed record HealthResponse
{
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
