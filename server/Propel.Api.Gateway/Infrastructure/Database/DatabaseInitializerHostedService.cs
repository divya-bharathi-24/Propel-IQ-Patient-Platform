namespace Propel.Api.Gateway.Infrastructure.Database;

/// <summary>
/// Hosted service that runs database initialization on application startup.
/// Ensures migrations are applied before the application starts accepting requests.
/// </summary>
public sealed class DatabaseInitializerHostedService : IHostedService
{
    private readonly DatabaseInitializer _initializer;
    private readonly ILogger<DatabaseInitializerHostedService> _logger;

    public DatabaseInitializerHostedService(
        DatabaseInitializer initializer,
        ILogger<DatabaseInitializerHostedService> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running database initialization on startup...");

        try
        {
            await _initializer.InitializeAsync(cancellationToken);
            _logger.LogInformation("Database initialization completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed. Application startup will continue, but database may not be accessible.");
            // Don't throw - allow app to start even if DB init fails
            // Health checks will report the issue
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Database initializer service stopping.");
        return Task.CompletedTask;
    }
}
