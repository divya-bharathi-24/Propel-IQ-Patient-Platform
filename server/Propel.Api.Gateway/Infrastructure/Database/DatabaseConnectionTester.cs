using Microsoft.EntityFrameworkCore;
using Npgsql;
using Propel.Api.Gateway.Data;

namespace Propel.Api.Gateway.Infrastructure.Database;

/// <summary>
/// Utility class for testing and diagnosing database connectivity issues.
/// Provides detailed connection diagnostics and troubleshooting information.
/// </summary>
public sealed class DatabaseConnectionTester
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseConnectionTester> _logger;

    public DatabaseConnectionTester(
        IDbContextFactory<AppDbContext> contextFactory,
        IConfiguration configuration,
        ILogger<DatabaseConnectionTester> logger)
    {
        _contextFactory = contextFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Performs comprehensive database connectivity tests and returns detailed diagnostics.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = new ConnectionTestResult();

        try
        {
            // Test 1: Get connection string
            _logger.LogInformation("Test 1: Checking connection string configuration...");
            var connectionString = GetConnectionString();
            result.HasConnectionString = !string.IsNullOrWhiteSpace(connectionString);
            result.ConnectionStringSource = GetConnectionStringSource();

            if (!result.HasConnectionString)
            {
                result.ErrorMessage = "No connection string found. Check DATABASE_URL or appsettings.json";
                return result;
            }

            // Test 2: Parse connection string
            _logger.LogInformation("Test 2: Parsing connection string...");
            var connectionInfo = ParseConnectionString(connectionString);
            result.ServerHost = connectionInfo.Host;
            result.ServerPort = connectionInfo.Port;
            result.DatabaseName = connectionInfo.Database;

            // Test 3: TCP connectivity
            _logger.LogInformation("Test 3: Testing TCP connectivity to {Host}:{Port}...", connectionInfo.Host, connectionInfo.Port);
            result.CanConnectToServer = await TestTcpConnectionAsync(connectionInfo.Host, connectionInfo.Port, cancellationToken);

            if (!result.CanConnectToServer)
            {
                result.ErrorMessage = $"Cannot connect to server {connectionInfo.Host}:{connectionInfo.Port}. Check if PostgreSQL is running.";
                return result;
            }

            // Test 4: Database connection
            _logger.LogInformation("Test 4: Testing database connection with EF Core...");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            result.CanConnectToDatabase = await context.Database.CanConnectAsync(cancellationToken);

            if (!result.CanConnectToDatabase)
            {
                result.ErrorMessage = "Server is reachable but cannot connect to database. Check credentials and database name.";
                return result;
            }

            // Test 5: Execute simple query
            _logger.LogInformation("Test 5: Executing test query...");
            var version = await context.Database.ExecuteSqlRawAsync("SELECT version();", cancellationToken);
            result.CanExecuteQuery = version >= 0;

            // Test 6: Check migrations
            _logger.LogInformation("Test 6: Checking migration status...");
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            result.AppliedMigrationsCount = appliedMigrations.Count();
            result.PendingMigrationsCount = pendingMigrations.Count();
            result.HasPendingMigrations = pendingMigrations.Any();

            // Test 7: Check extensions
            _logger.LogInformation("Test 7: Checking PostgreSQL extensions...");
            result.ExtensionsInstalled = await CheckExtensionsAsync(context, cancellationToken);

            result.IsFullyOperational = result.CanConnectToDatabase && 
                                       result.CanExecuteQuery && 
                                       !result.HasPendingMigrations;

            _logger.LogInformation("Connection test completed successfully. Status: {Status}", 
                result.IsFullyOperational ? "Fully Operational" : "Operational with Warnings");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed: {Message}", ex.Message);
            result.ErrorMessage = ex.Message;
            result.ExceptionDetails = ex.ToString();
        }

        return result;
    }

    private string GetConnectionString()
    {
        return _configuration["DATABASE_URL"] 
            ?? _configuration.GetConnectionString("DefaultConnection") 
            ?? string.Empty;
    }

    private string GetConnectionStringSource()
    {
        if (!string.IsNullOrWhiteSpace(_configuration["DATABASE_URL"]))
            return "Environment Variable (DATABASE_URL)";
        if (!string.IsNullOrWhiteSpace(_configuration.GetConnectionString("DefaultConnection")))
            return "appsettings.json (ConnectionStrings:DefaultConnection)";
        return "Not Found";
    }

    private (string Host, int Port, string Database) ParseConnectionString(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return (builder.Host ?? "unknown", builder.Port, builder.Database ?? "unknown");
        }
        catch
        {
            return ("unknown", 5432, "unknown");
        }
    }

    private async Task<bool> TestTcpConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<string>> CheckExtensionsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var installedExtensions = new List<string>();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT extname FROM pg_extension WHERE extname IN ('pgcrypto', 'vector');";
            await context.Database.OpenConnectionAsync(cancellationToken);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                installedExtensions.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Extensions check is optional
        }

        return installedExtensions;
    }
}

/// <summary>
/// Results from database connection testing.
/// </summary>
public sealed class ConnectionTestResult
{
    public bool HasConnectionString { get; set; }
    public string ConnectionStringSource { get; set; } = string.Empty;
    public string? ServerHost { get; set; }
    public int ServerPort { get; set; }
    public string? DatabaseName { get; set; }
    public bool CanConnectToServer { get; set; }
    public bool CanConnectToDatabase { get; set; }
    public bool CanExecuteQuery { get; set; }
    public int AppliedMigrationsCount { get; set; }
    public int PendingMigrationsCount { get; set; }
    public bool HasPendingMigrations { get; set; }
    public List<string> ExtensionsInstalled { get; set; } = new();
    public bool IsFullyOperational { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExceptionDetails { get; set; }

    public string GetStatusSummary()
    {
        if (!string.IsNullOrWhiteSpace(ErrorMessage))
            return $"❌ Failed: {ErrorMessage}";

        if (IsFullyOperational)
            return "✅ Fully Operational";

        if (HasPendingMigrations)
            return $"⚠️ Operational - {PendingMigrationsCount} pending migration(s)";

        return "⚠️ Operational with Warnings";
    }
}
