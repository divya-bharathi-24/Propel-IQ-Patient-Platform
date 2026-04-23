# Quick Start Guide - Updated (Redis Disabled)

## Prerequisites
- ? Visual Studio 2022 or later
- ? .NET 10 SDK
- ? PostgreSQL running on port 5434
- ? ~~Redis~~ - **NOT REQUIRED** (disabled in development)
- ? ~~Docker~~ - **NOT REQUIRED** (Redis disabled)

## Start the Application

### Step 1: Apply Database Migration (One-time)

If you haven't already applied the `patient_id` migration:

**Option A: Using pgAdmin/DBeaver (Recommended)**
1. Open pgAdmin or DBeaver
2. Connect to `propeliq_dev` database
3. Execute the SQL from `add_patient_id_to_refresh_tokens.sql`

**Option B: Using EF Migrations**
```powershell
cd Propel.Api.Gateway
dotnet ef database update
```

### Step 2: Start the Application

1. Open `Propel-IQ-Patient-Platform.sln` in Visual Studio
2. Press **F5** to start debugging
3. Wait for the application to start

### Step 3: Verify Startup

Look for these log messages:
```
[WARN] DEVELOPMENT MODE: Redis is disabled. Using IN-MEMORY session storage.
[INFO] Session service: Using IN-MEMORY storage (development mode)
[INFO] Data Protection: Persisting keys to ...
[Startup] Migrations applied successfully.
```

### Step 4: Test Login

1. Navigate to Swagger UI: `https://localhost:7213/swagger`
2. Use the `/api/auth/login` endpoint
3. Login with your patient credentials
4. You should receive an access token and refresh token

## What Changed

### ? Removed Requirements
- Redis is no longer required
- Docker is no longer required
- No need to run `start-redis.ps1`

### ? What Works
- Login and authentication
- Session management (in-memory)
- All API endpoints
- Database operations
- PHI encryption/decryption

### ?? Important Notes
- Sessions are stored **in memory**
- Sessions are **lost when you restart** the application
- This is **development mode only** - Production requires Redis

## Troubleshooting

### Application fails to start
```
Error: column "patient_id" does not exist
```
**Solution:** Apply the database migration (see Step 1)

### Still seeing Redis errors
```
Error: RedisConnectionException
```
**Solution:** Make sure you're running the **latest code** from this fix. Rebuild the solution:
```powershell
dotnet clean
dotnet build
```

### Login fails with validation error
**Solution:** Check that you're using valid credentials:
```json
{
  "email": "patient@example.com",
  "password": "ValidPassword123!",
  "deviceId": "test-device"
}
```

## Files Modified

This update modified the following files:
- `Propel.Api.Gateway/Program.cs` - Disabled Redis in development
- `Propel.Api.Gateway/appsettings.Development.json` - Removed Redis config
- `REDIS_DISABLED.md` - Documentation

## Next Steps

Once the application is running:
1. Test the login endpoint
2. Test the refresh token endpoint
3. Test other API endpoints
4. Start developing your features

## Need Help?

Check these files for more information:
- `REDIS_DISABLED.md` - Detailed explanation of Redis changes
- `COMPLETE_FIX_SUMMARY.md` - Summary of all fixes applied
- `REFRESH_TOKEN_MIGRATION_FIX.md` - Database migration details

---

**Last Updated:** After disabling Redis in development mode  
**Status:** ? Ready for development (no external dependencies required)
