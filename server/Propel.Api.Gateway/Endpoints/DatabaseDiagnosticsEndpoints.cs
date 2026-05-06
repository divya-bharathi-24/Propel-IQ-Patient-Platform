using Microsoft.AspNetCore.Mvc;
using Propel.Api.Gateway.Infrastructure.Database;

namespace Propel.Api.Gateway.Endpoints;

/// <summary>
/// Diagnostic endpoints for database troubleshooting and testing.
/// </summary>
public static class DatabaseDiagnosticsEndpoints
{
    public static void MapDatabaseDiagnostics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/database/diagnostics")
            .WithTags("Database Diagnostics");

        // GET /api/database/diagnostics/test - Comprehensive connection test
        group.MapGet("/test", RunConnectionTest)
            .WithName("TestDatabaseConnection")
            .WithSummary("Run comprehensive database connection diagnostics")
            .Produces<ConnectionTestResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        // GET /api/database/diagnostics/info - Get database information
        group.MapGet("/info", GetDatabaseInfo)
            .WithName("GetDatabaseInfo")
            .WithSummary("Get detailed database configuration information")
            .Produces<DatabaseInfoResponse>(StatusCodes.Status200OK);

        // POST /api/database/diagnostics/fix-missing-column - Fix missing pending_alerts_json column
        group.MapPost("/fix-missing-column", FixMissingColumn)
            .WithName("FixMissingColumn")
            .WithSummary("Fix the missing pending_alerts_json column issue")
            .Produces<FixColumnResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }

    private static async Task<IResult> RunConnectionTest(
        DatabaseConnectionTester tester,
        CancellationToken cancellationToken)
    {
        try
        {
            var testResult = await tester.TestConnectionAsync(cancellationToken);

            var response = new ConnectionTestResponse
            {
                Success = testResult.IsFullyOperational,
                Summary = testResult.GetStatusSummary(),
                ConnectionStringSource = testResult.ConnectionStringSource,
                ServerHost = testResult.ServerHost,
                ServerPort = testResult.ServerPort,
                DatabaseName = testResult.DatabaseName,
                Tests = new TestResults
                {
                    HasConnectionString = testResult.HasConnectionString,
                    CanConnectToServer = testResult.CanConnectToServer,
                    CanConnectToDatabase = testResult.CanConnectToDatabase,
                    CanExecuteQuery = testResult.CanExecuteQuery,
                    AppliedMigrationsCount = testResult.AppliedMigrationsCount,
                    PendingMigrationsCount = testResult.PendingMigrationsCount,
                    HasPendingMigrations = testResult.HasPendingMigrations,
                    ExtensionsInstalled = testResult.ExtensionsInstalled
                },
                ErrorMessage = testResult.ErrorMessage,
                Recommendations = GenerateRecommendations(testResult)
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Diagnostics test failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult GetDatabaseInfo(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration["DATABASE_URL"] 
            ?? configuration.GetConnectionString("DefaultConnection");

        string? serverHost = null;
        int serverPort = 0;
        string? databaseName = null;
        string? userId = null;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                serverHost = builder.Host;
                serverPort = builder.Port;
                databaseName = builder.Database;
                userId = builder.Username;
            }
            catch
            {
                serverHost = "Unable to parse";
            }
        }

        var response = new DatabaseInfoResponse
        {
            Environment = environment.EnvironmentName,
            HasConnectionString = !string.IsNullOrWhiteSpace(connectionString),
            ConnectionStringSource = GetConnectionStringSource(configuration),
            Provider = "Npgsql (PostgreSQL)",
            ContextType = "Propel.Api.Gateway.Data.AppDbContext",
            MigrationAssembly = "Propel.Api.Gateway",
            PoolingEnabled = true,
            MaxPoolSize = 50,
            MinPoolSize = 5,
            ServerHost = serverHost,
            ServerPort = serverPort,
            DatabaseName = databaseName,
            UserId = userId
        };

        return Results.Ok(response);
    }

    private static string GetConnectionStringSource(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["DATABASE_URL"]))
            return "DATABASE_URL (Environment Variable)";
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            return "appsettings.json";
        return "Not configured";
    }

    private static List<string> GenerateRecommendations(ConnectionTestResult result)
    {
        var recommendations = new List<string>();

        if (!result.HasConnectionString)
        {
            recommendations.Add("Configure DATABASE_URL environment variable or set ConnectionStrings:DefaultConnection in appsettings.json");
        }

        if (!result.CanConnectToServer)
        {
            recommendations.Add($"Ensure PostgreSQL is running on {result.ServerHost}:{result.ServerPort}");
            recommendations.Add("Check firewall settings and network connectivity");
            recommendations.Add("Verify the server host and port are correct");
        }

        if (result.CanConnectToServer && !result.CanConnectToDatabase)
        {
            recommendations.Add("Verify database credentials (username/password)");
            recommendations.Add($"Ensure database '{result.DatabaseName}' exists");
            recommendations.Add("Check user permissions on the database");
        }

        if (result.HasPendingMigrations)
        {
            recommendations.Add($"Apply {result.PendingMigrationsCount} pending migration(s) by running: dotnet ef database update");
            recommendations.Add("Or POST to /api/database/migrate endpoint");
            recommendations.Add("Or restart the application (automatic migration)");
        }

        if (!result.ExtensionsInstalled.Contains("pgcrypto"))
        {
            recommendations.Add("Install pgcrypto extension: CREATE EXTENSION IF NOT EXISTS pgcrypto;");
        }

        if (result.IsFullyOperational)
        {
            recommendations.Add("✅ Database is fully operational and ready for use!");
        }

        return recommendations;
    }

    private static async Task<IResult> FixMissingColumn(
        Propel.Api.Gateway.Infrastructure.Database.DatabaseColumnFixer fixer,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await fixer.FixMissingPendingAlertsColumnAsync(cancellationToken);

            var response = new FixColumnResponse
            {
                Success = result.Success,
                Message = result.Message,
                ColumnWasMissing = !result.ColumnExistedBefore,
                ColumnAdded = result.ColumnAdded,
                MigrationRecordWasMissing = !result.MigrationExistedBefore,
                MigrationRecordAdded = result.MigrationRecordAdded,
                ErrorDetails = result.ErrorDetails
            };

            if (!result.Success)
            {
                return Results.Problem(
                    title: "Failed to fix missing column",
                    detail: result.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Fix operation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public sealed record FixColumnResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool ColumnWasMissing { get; init; }
    public bool ColumnAdded { get; init; }
    public bool MigrationRecordWasMissing { get; init; }
    public bool MigrationRecordAdded { get; init; }
    public string? ErrorDetails { get; init; }
}

public sealed record ConnectionTestResponse
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string ConnectionStringSource { get; init; } = string.Empty;
    public string? ServerHost { get; init; }
    public int ServerPort { get; init; }
    public string? DatabaseName { get; init; }
    public TestResults Tests { get; init; } = new();
    public string? ErrorMessage { get; init; }
    public List<string> Recommendations { get; init; } = new();
}

public sealed record TestResults
{
    public bool HasConnectionString { get; init; }
    public bool CanConnectToServer { get; init; }
    public bool CanConnectToDatabase { get; init; }
    public bool CanExecuteQuery { get; init; }
    public int AppliedMigrationsCount { get; init; }
    public int PendingMigrationsCount { get; init; }
    public bool HasPendingMigrations { get; init; }
    public List<string> ExtensionsInstalled { get; init; } = new();
}

public sealed record DatabaseInfoResponse
{
    public string Environment { get; init; } = string.Empty;
    public bool HasConnectionString { get; init; }
    public string ConnectionStringSource { get; init; } = string.Empty;
    public string? ServerHost { get; init; }
    public int ServerPort { get; init; }
    public string? DatabaseName { get; init; }
    public string? UserId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ContextType { get; init; } = string.Empty;
    public string MigrationAssembly { get; init; } = string.Empty;
    public bool PoolingEnabled { get; init; }
    public int MaxPoolSize { get; init; }
    public int MinPoolSize { get; init; }
}
