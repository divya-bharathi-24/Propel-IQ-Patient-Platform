# Database Quick Reference

## Common Issues & Quick Fixes

### ✅ Everything is Working
Run the health check script:
```powershell
.\check-database-health.ps1
```

### ❌ Column Type Mismatch Error
**Symptom:** Error like `column "xxx" is of type date but expression is of type text`

**Quick Fix:**
```powershell
# Run schema verification
dotnet run --project .\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj

# If issues found, apply migrations
cd Propel.Api.Gateway
dotnet ef database update
cd ..
```

### ❌ Column Does Not Exist Error
**Symptom:** Error like `column "patient_id" of relation "refresh_tokens" does not exist`

**Quick Fix:**
```powershell
# Apply all pending migrations
cd Propel.Api.Gateway
dotnet ef database update
cd ..

# Sync migration history
dotnet run --project .\SyncMigrationHistory\SyncMigrationHistory.csproj
```

### ⚠️ Fresh Database Setup
**Starting from scratch:**
```powershell
cd Propel.Api.Gateway

# Drop database (WARNING: Deletes all data!)
dotnet ef database drop --force

# Recreate and apply all migrations
dotnet ef database update

cd ..
```

### 🔄 Reset to Latest Schema
**After pulling new code:**
```powershell
cd Propel.Api.Gateway

# Check what migrations are pending
dotnet ef migrations list

# Apply any pending migrations
dotnet ef database update

cd ..
```

---

## Database Connection

**Connection String (Development):**
```
Server=127.0.0.1;Port=5433;User Id=postgres;Password=admin;Database=propeliq_dev1;
```

**Location:** `Propel.Api.Gateway/appsettings.Development.json`

---

## Useful Commands

### Check Migration Status
```bash
cd Propel.Api.Gateway
dotnet ef migrations list
```

### View Last Applied Migration
```bash
cd Propel.Api.Gateway
dotnet ef migrations list | Select-Object -Last 1
```

### Generate SQL Script (without applying)
```bash
cd Propel.Api.Gateway
dotnet ef migrations script > migration-script.sql
```

### Check Database Connection
```bash
cd Propel.Api.Gateway
dotnet ef database drop --dry-run
```

---

## Verification Tools

### 1. Health Check (Quick)
```powershell
.\check-database-health.ps1
```

### 2. Schema Verification (Detailed)
```powershell
dotnet run --project .\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj
```

### 3. Migration History Sync
```powershell
dotnet run --project .\SyncMigrationHistory\SyncMigrationHistory.csproj
```

---

## Key Files & Paths

| File | Description |
|------|-------------|
| `DATABASE_SCHEMA_FIX_SUMMARY.md` | Detailed fix documentation |
| `check-database-health.ps1` | Quick health check script |
| `VerifyDatabaseSchema/` | Schema verification tool |
| `SyncMigrationHistory/` | Migration history sync tool |
| `Propel.Api.Gateway/Migrations/` | All EF Core migrations |
| `Propel.Api.Gateway/Data/AppDbContext.cs` | DbContext with PHI encryption |

---

## Critical Schema Requirements

### PHI Encrypted Columns (Must be TEXT)
- ✅ `patients.name` → `text`
- ✅ `patients.phone` → `text`
- ✅ `patients.date_of_birth` → `text`

### Refresh Token Schema
- ✅ `refresh_tokens.patient_id` → `uuid` (nullable)
- ✅ `refresh_tokens.user_id` → `uuid` (nullable)
- ✅ CHECK constraint: exactly one of patient_id or user_id must be non-null

---

## Troubleshooting Checklist

- [ ] Build is successful (`dotnet build`)
- [ ] Database server is running (PostgreSQL on port 5433)
- [ ] Connection string is correct in `appsettings.Development.json`
- [ ] All migrations are applied (`dotnet ef database update`)
- [ ] Schema verification passes (`.\check-database-health.ps1`)
- [ ] Migration history is synchronized

---

## Support

**Current Status:** ✅ All systems operational  
**Migrations Applied:** 27  
**Last Verified:** Run `.\check-database-health.ps1` to check

For detailed information about the recent fixes, see: `DATABASE_SCHEMA_FIX_SUMMARY.md`
