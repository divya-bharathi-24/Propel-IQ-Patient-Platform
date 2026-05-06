# Database Management Guide

This guide explains how to manage database connections, migrations, and synchronization in the Propel IQ platform.

## Overview

The platform includes automated database management features that handle:
- **Automatic migration application** on startup
- **Database connectivity checks** and health monitoring
- **Migration status tracking** and reporting
- **Manual migration triggers** via API endpoints

## Automatic Database Initialization

When the application starts, the `DatabaseInitializerHostedService` automatically:

1. ✅ Verifies database connectivity
2. ✅ Checks for pending migrations
3. ✅ Applies all pending migrations
4. ✅ Seeds reference/master data
5. ✅ Logs detailed status information

**No manual intervention is required** - migrations are applied automatically before the application accepts requests.

## Configuration

### Connection String

The database connection is configured via environment variables:

**Development (appsettings.Development.json):**
```json
{
  "ConnectionStrings": {
	"DefaultConnection": "Server=127.0.0.1;Port=5434;User Id=postgres;Password=admin;Database=propeliq_dev;"
  }
}
```

**Production (Environment Variable):**
```bash
DATABASE_URL=postgres://username:password@host:port/database
```

### Connection Pool Settings

The application automatically configures connection pooling:
- **Maximum Pool Size:** 50 connections
- **Minimum Pool Size:** 5 connections (pre-warmed)
- **Connection Idle Lifetime:** 300 seconds
- **Connection Pruning Interval:** 60 seconds

## API Endpoints

### 1. Check Database Status

**GET** `/api/database/status`

Returns detailed migration status including applied and pending migrations.

**Response:**
```json
{
  "isConnected": true,
  "message": "Database is up-to-date",
  "appliedMigrationsCount": 45,
  "pendingMigrationsCount": 0,
  "lastAppliedMigration": "20260423184303_AddAiQualityMetricsTable",
  "appliedMigrations": [...],
  "pendingMigrations": []
}
```

### 2. Apply Migrations Manually

**POST** `/api/database/migrate`

Triggers migration application (useful for manual deployment scenarios).

**Response:**
```json
{
  "success": true,
  "message": "Successfully applied 2 migration(s).",
  "migrationsApplied": 2,
  "appliedMigrations": [
	"20260424000000_NewMigration1",
	"20260424000001_NewMigration2"
  ]
}
```

### 3. Check Database Health

**GET** `/api/database/health`

Simple connectivity check for health monitoring.

**Response:**
```json
{
  "status": "Healthy",
  "message": "Database is connected and accessible"
}
```

## Creating New Migrations

### Using .NET CLI

From the `server/Propel.Api.Gateway` directory:

```bash
# Create a new migration
dotnet ef migrations add MigrationName

# Remove the last migration (if not applied)
dotnet ef migrations remove

# View migration SQL without applying
dotnet ef migrations script
```

### Using Visual Studio

1. Open **Package Manager Console** (Tools → NuGet Package Manager → Package Manager Console)
2. Set Default Project to `Propel.Api.Gateway`
3. Run commands:

```powershell
# Create a new migration
Add-Migration MigrationName

# Remove the last migration
Remove-Migration

# View migration SQL
Script-Migration
```

## Database Synchronization Workflow

### Development Environment

1. **Start the application** - migrations apply automatically
2. **Make schema changes** to entity configurations
3. **Create migration**: `dotnet ef migrations add MigrationName`
4. **Restart application** - new migration applies automatically

### Production Environment

#### Option 1: Automatic (Recommended)
- Deploy the application with new migrations
- Migrations apply automatically on startup
- No manual intervention required

#### Option 2: Manual Control
1. Deploy application (keep offline or in maintenance mode)
2. Call `POST /api/database/migrate` to apply migrations
3. Verify with `GET /api/database/status`
4. Bring application online

## Health Checks

The database health is monitored through multiple endpoints:

### 1. Platform Health Check
**GET** `/health`

Includes PostgreSQL and pgcrypto checks. Returns:
- **200 OK**: Database is healthy
- **503 Service Unavailable**: Database is down

### 2. Liveness Probe
**GET** `/health/live`

Critical database-only check for container orchestrators.

### 3. Detailed Health
**GET** `/healthz`

Detailed JSON response with database status.

### 4. Database-Specific Health
**GET** `/api/database/health`

Direct database connectivity check.

## Troubleshooting

### Cannot Connect to Database

**Symptoms:**
- Application fails to start
- 503 errors on health endpoints
- Connection timeout errors

**Solutions:**

1. **Verify connection string:**
   ```bash
   # Development
   cat appsettings.Development.json | grep DefaultConnection

   # Production
   echo $DATABASE_URL
   ```

2. **Check database server:**
   ```bash
   # PostgreSQL running?
   docker ps | grep postgres

   # Can connect manually?
   psql -h 127.0.0.1 -p 5434 -U postgres -d propeliq_dev
   ```

3. **Check firewall/network:**
   ```bash
   # Test port connectivity
   telnet 127.0.0.1 5434
   ```

### Migrations Not Applying

**Symptoms:**
- Application starts but database schema is outdated
- "Pending migrations" warnings in logs

**Solutions:**

1. **Check migration status:**
   ```bash
   curl http://localhost:5000/api/database/status
   ```

2. **Manually trigger migrations:**
   ```bash
   curl -X POST http://localhost:5000/api/database/migrate
   ```

3. **Check logs:**
   ```bash
   # Look for migration errors
   grep "migration" logs/application.log
   ```

4. **Reset database (DEVELOPMENT ONLY):**
   ```bash
   dotnet ef database drop --force
   # Restart application - migrations will recreate schema
   ```

### Migration Conflicts

**Symptoms:**
- Multiple developers created migrations simultaneously
- Merge conflicts in migration files

**Solutions:**

1. **Coordinate migration creation** through your team
2. **Remove conflicting migration:**
   ```bash
   dotnet ef migrations remove
   ```
3. **Pull latest code** and recreate migration
4. **Test migration** in a clean database

### Performance Issues

**Symptoms:**
- Slow queries
- Connection pool exhaustion
- Timeout errors

**Solutions:**

1. **Check connection pool usage:**
   - Monitor logs for pool warnings
   - Adjust pool size if needed (currently: max 50, min 5)

2. **Optimize queries:**
   - Enable query logging in development
   - Use database performance monitoring tools

3. **Add indexes:**
   - Create migration with appropriate indexes
   - Monitor slow query logs

## Best Practices

### ✅ DO:
- Let migrations apply automatically on startup
- Create descriptive migration names
- Test migrations in development first
- Use the status endpoint to verify deployment
- Monitor health checks continuously

### ❌ DON'T:
- Modify applied migrations
- Apply migrations manually unless necessary
- Skip migration testing
- Ignore health check failures
- Use `EnsureCreated()` (migrations only!)

## Security Considerations

### Connection Security
- ✅ Connection strings stored in environment variables
- ✅ Passwords never committed to source control
- ✅ TLS/SSL required for production connections
- ✅ Connection pooling prevents exhaustion attacks

### API Security
- ⚠️ Database management endpoints are **publicly accessible**
- 🔒 Consider adding authentication/authorization for production
- 📊 All operations are logged for audit trail

### Data Protection
- ✅ PHI fields encrypted via pgcrypto
- ✅ Encryption keys managed via Data Protection
- ✅ Audit logging enabled for all operations

## Monitoring and Observability

### Key Metrics to Monitor

1. **Database Connectivity**
   - Monitor `/health/live` endpoint
   - Alert on 503 responses

2. **Migration Status**
   - Check `/api/database/status` after deployments
   - Verify pendingMigrationsCount = 0

3. **Connection Pool**
   - Monitor pool exhaustion warnings
   - Track connection acquisition times

4. **Query Performance**
   - Enable slow query logging
   - Monitor query execution times

### Logging

All database operations are logged with Serilog:

```
[12:34:56 INF] Starting database initialization...
[12:34:56 INF] Database status: 45 migrations applied, 0 pending
[12:34:56 INF] ✓ Database migrations applied successfully.
[12:34:56 INF] ✓ Database schema is current: 20260423184303_AddAiQualityMetricsTable
[12:34:56 INF] ✓ Seed data process completed successfully.
[12:34:56 INF] Database initialization complete. Application is ready to accept requests.
```

## Additional Resources

- [Entity Framework Core Documentation](https://docs.microsoft.com/ef/core/)
- [PostgreSQL Connection Pooling](https://www.npgsql.org/doc/connection-string-parameters.html)
- [ASP.NET Core Health Checks](https://docs.microsoft.com/aspnet/core/host-and-deploy/health-checks)

## Support

For additional help:
1. Check application logs for detailed error messages
2. Review this documentation for troubleshooting steps
3. Contact the development team with specific error details
