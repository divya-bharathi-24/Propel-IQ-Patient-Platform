using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;
// TODO: Uncomment when pgvector is installed and AI features are ready
// using Pgvector.EntityFrameworkCore;
// using Pgvector.Npgsql;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Security;

namespace Propel.Api.Gateway.Data;

/// <summary>
/// Design-time factory used by the <c>dotnet ef</c> CLI to construct an
/// <see cref="AppDbContext"/> without invoking <c>Program.cs</c>.
/// This decouples migration generation from the runtime DI bootstrapping and
/// the startup env-var guards defined in <c>Program.cs</c> (task_003).
///
/// Connection string resolution order:
///   1. <c>DATABASE_URL</c> environment variable (CI / staging).
///   2. <c>ConnectionStrings:DefaultConnection</c> in <c>appsettings.Development.json</c>.
///   3. Hard-coded fallback for isolated local CLI usage.
///
/// The factory reads <c>appsettings.Development.json</c> from the project root,
/// which is gitignored and safe to contain real dev credentials.
///
/// <c>UseVector()</c> is called on the <see cref="NpgsqlDataSourceBuilder"/> so that the
/// design-time context can resolve <c>float[]</c> ↔ <c>vector(1536)</c> column mappings
/// required by <see cref="Propel.Domain.Entities.ExtractedData.Embedding"/> (AC-2, task_003).
/// [TEMPORARILY DISABLED - AI features commented out]
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // 1. Environment variable (CI pipeline / Railway staging)
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // 2. appsettings.Development.json (local dev — gitignored)
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            connectionString = config.GetConnectionString("DefaultConnection");
        }

        // 3. Hard-coded fallback so CLI never fails with a missing connection string
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                "Host=localhost;Port=5432;Database=propeliq_dev;Username=postgres;Password=dev";
        }

        // TODO: Uncomment when pgvector is installed and AI features are ready
        // UseVector() registers the pgvector type handler on the Npgsql data source so
        // EF Core can resolve float[] ↔ vector(1536) at design time (migration generation).
        // This mirrors the runtime registration in Program.cs (task_002, AC-2).
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        // dataSourceBuilder.UseVector();  // COMMENTED OUT - AI features disabled temporarily
        var dataSource = dataSourceBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder
            // TODO: Uncomment when pgvector is installed and AI features are ready
            // UseVector() registers EF Core type mappings for Pgvector.Vector ↔ vector(N) (task_003, AC-2).
            .UseNpgsql(dataSource /*, o => o.UseVector() */)  // COMMENTED OUT - AI features disabled temporarily
            .UseSnakeCaseNamingConvention();

        return new AppDbContext(
            optionsBuilder.Options,
            // Design-time only: ephemeral key provider — never used for real encryption;
            // migrations do not execute value converters (no DB data is read/written).
            new AesGcmPhiEncryptionService(new EphemeralDataProtectionProvider()));
    }
}
