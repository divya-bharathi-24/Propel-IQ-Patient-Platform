# Database Management - Quick Start Guide

## 🚀 Getting Started

The Propel IQ platform includes automated database management. **Migrations are applied automatically** when you start the application - no manual steps required!

## ✨ Key Features

- ✅ **Automatic migrations** on application startup
- ✅ **Health monitoring** with multiple endpoints
- ✅ **API endpoints** for status checking and manual control
- ✅ **PowerShell helper script** for common tasks
- ✅ **Comprehensive logging** of all database operations

## 📋 Common Tasks

### Check Database Status

**Using PowerShell Script:**
```powershell
.\db-manage.ps1 status
```

**Using API:**
```bash
curl http://localhost:5000/api/database/status
```

**Using Browser:**
```
http://localhost:5000/api/database/status
```

### Create a New Migration

**Using PowerShell Script:**
```powershell
.\db-manage.ps1 create "AddNewFeature"
```

**Using .NET CLI:**
```bash
cd Propel.Api.Gateway
dotnet ef migrations add AddNewFeature
```

### Apply Migrations

**Automatic (Recommended):**
Just start or restart the application - migrations apply automatically!

**Manual:**
```powershell
.\db-manage.ps1 migrate
```

### Check Database Health

**Using PowerShell Script:**
```powershell
.\db-manage.ps1 health
```

**Using API:**
```bash
curl http://localhost:5000/api/database/health
```

## 🔧 Development Workflow

### Making Schema Changes

1. **Modify entity classes** in `Propel.Domain/Entities/`
2. **Update configurations** if needed in `Propel.Api.Gateway/Data/Configurations/`
3. **Create migration:**
   ```powershell
   .\db-manage.ps1 create "DescriptiveChangeName"
   ```
4. **Restart the app** - migration applies automatically
5. **Verify** with `.\db-manage.ps1 status`

### Starting Fresh (Development Only)

**Warning:** This deletes ALL data!

```powershell
.\db-manage.ps1 reset
```

Then restart the application to recreate the database.

## 🌐 API Endpoints

All endpoints are available at `/api/database/*`:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/database/status` | GET | Get detailed migration status |
| `/api/database/migrate` | POST | Manually apply pending migrations |
| `/api/database/health` | GET | Check database connectivity |

## 📊 Monitoring

### Health Check Endpoints

The platform provides multiple health check endpoints:

- **`/health`** - Full platform health (all services)
- **`/health/live`** - Database-only liveness probe
- **`/healthz`** - Detailed health with JSON response
- **`/api/database/health`** - Database-specific health

### Logs

All database operations are logged to console and Serilog:

```
[12:34:56 INF] Starting database initialization...
[12:34:56 INF] Database status: 45 migrations applied, 0 pending
[12:34:56 INF] ✓ Database migrations applied successfully.
[12:34:56 INF] ✓ Database schema is current
```

## ⚠️ Troubleshooting

### Cannot Connect to Database

1. **Check PostgreSQL is running:**
   ```bash
   docker ps | grep postgres
   ```

2. **Verify connection string** in `appsettings.Development.json`

3. **Test connectivity:**
   ```powershell
   .\db-manage.ps1 health
   ```

### Migrations Not Applying

1. **Check status:**
   ```powershell
   .\db-manage.ps1 status
   ```

2. **View application logs** for errors

3. **Manually trigger:**
   ```powershell
   .\db-manage.ps1 migrate
   ```

### Migration Conflicts

If you have merge conflicts in migration files:

1. **Remove your migration:**
   ```powershell
   .\db-manage.ps1 remove
   ```

2. **Pull latest code** from main branch

3. **Recreate your migration:**
   ```powershell
   .\db-manage.ps1 create "YourFeatureName"
   ```

## 📖 Full Documentation

See [DATABASE_MANAGEMENT.md](./DATABASE_MANAGEMENT.md) for complete documentation including:
- Detailed configuration options
- Production deployment strategies
- Security considerations
- Performance tuning
- Advanced troubleshooting

## 🛠️ PowerShell Script Reference

The `db-manage.ps1` script provides convenient commands:

```powershell
# Get help
.\db-manage.ps1 help

# Check migration status
.\db-manage.ps1 status

# Apply pending migrations
.\db-manage.ps1 migrate

# Create new migration
.\db-manage.ps1 create "MigrationName"

# Remove last migration (if not applied)
.\db-manage.ps1 remove

# Reset database (DEVELOPMENT ONLY)
.\db-manage.ps1 reset

# Check database health
.\db-manage.ps1 health
```

## 🔒 Security Notes

- ⚠️ Database management endpoints are **publicly accessible** by default
- 🔐 Consider adding authentication for production environments
- ✅ All operations are logged for audit trail
- ✅ Connection strings are stored in environment variables (never in code)

## 📞 Need Help?

- Check the full documentation: [DATABASE_MANAGEMENT.md](./DATABASE_MANAGEMENT.md)
- Review application logs for error details
- Contact the development team with specific error messages

---

**Remember:** The database is automatically synchronized on startup. You usually don't need to manually manage migrations unless you're in a special deployment scenario!
