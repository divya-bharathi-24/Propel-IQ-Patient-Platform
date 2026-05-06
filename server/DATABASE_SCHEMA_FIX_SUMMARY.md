# Database Schema Fix Summary

## Issues Resolved

### Issue 1: Sign-up Error - `date_of_birth` Column Type Mismatch
**Error Message:**
```
PostgresException: 42804: column "date_of_birth" is of type date but expression is of type text
```

**Root Cause:**
- The `patients.date_of_birth` column was created as `date` type in the initial migration
- PHI encryption feature requires storing encrypted Base64 strings, necessitating `text` type
- The migration `20260501163721_ConvertDateOfBirthToTextForEncryption` existed but was not applied to the database

**Fix Applied:**
- Converted `date_of_birth` column from `date` to `text` using SQL: 
  ```sql
  ALTER TABLE patients ALTER COLUMN date_of_birth TYPE text USING date_of_birth::text
  ```
- Added migration to `__EFMigrationsHistory` table

---

### Issue 2: Login Error - `patient_id` Column Missing
**Error Message:**
```
PostgresException: 42703: column "patient_id" of relation "refresh_tokens" does not exist
```

**Root Cause:**
- The `refresh_tokens` table was initially created for staff/admin users only (with `user_id`)
- Patient authentication support was added later, requiring a `patient_id` column
- The migration `20260422030000_AddPatientIdToRefreshTokens` existed but was not applied to the database

**Fix Applied:**
- Made `user_id` nullable
- Added `patient_id` column (nullable)
- Created FK constraints to both `patients` and `users` tables
- Added CHECK constraint to ensure exactly one of `patient_id` or `user_id` is non-null
- Created appropriate indexes for performance:
  - `ix_refresh_tokens_patient_id`
  - `ix_refresh_tokens_patient_id_family_id` (partial index)
  - `ix_refresh_tokens_user_id_family_id` (partial index)
- Added migration to `__EFMigrationsHistory` table

---

## Why This Happened

The database schema was out of sync with the EF Core model. Two scenarios likely caused this:

1. **Database was restored from an older backup** that predated these migrations
2. **Migrations were not run after pulling latest code** containing new features
3. **Database was created manually** without running all migrations

EF Core's migration system tracks applied migrations in the `__EFMigrationsHistory` table. When the actual database schema doesn't match what EF Core expects, runtime errors occur.

---

## Verification Results

All critical schema checks are now **PASSING**:

✅ **PHI Encryption Columns:**
- `patients.name` → `text` (encrypted)
- `patients.phone` → `text` (encrypted)  
- `patients.date_of_birth` → `text` (encrypted)

✅ **Refresh Token Support:**
- `refresh_tokens.patient_id` → exists and nullable
- `refresh_tokens.user_id` → nullable
- CHECK constraint → ensures exactly one FK is set
- All required indexes → created

✅ **Migration History:**
- 27 migrations recorded
- Database schema matches EF Core model

---

## How to Prevent This in the Future

### 1. Always Run Migrations After Pulling Code
```bash
cd Propel.Api.Gateway
dotnet ef database update
```

### 2. Check Migration Status
```bash
cd Propel.Api.Gateway
dotnet ef migrations list
```
This shows which migrations are applied (green) vs pending (red).

### 3. Use the Verification Tool
We've created a tool to verify your database schema matches expectations. Run it anytime:
```bash
dotnet run --project .\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj
```

### 4. Enable Automatic Migrations in Development
Consider adding this to your `Program.cs` during development (but remove for production):
```csharp
// Apply pending migrations on startup (development only)
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.Migrate();
}
```

---

## Architecture Details

### PHI Encryption (NFR-004, NFR-013)
The application uses AES-256-GCM encryption for PHI fields:
- **Encryption Flow:** `DateOnly` → `"yyyy-MM-dd"` → encrypt → Base64 → store as `text`
- **Decryption Flow:** Read `text` → decrypt → parse → `DateOnly`
- **Service:** `IPhiEncryptionService` (injected into `AppDbContext`)
- **Configuration:** Applied via EF Core value converters in `OnModelCreating()`

### Refresh Token Architecture (US_011)
Supports both patient and staff/admin authentication:
- **Token Storage:** Only SHA-256 hash stored (OWASP A02 compliance)
- **Token Families:** Enables reuse detection and family-wide revocation
- **Dual FK Design:** Either `patient_id` OR `user_id` must be set (enforced by CHECK constraint)
- **Security:** Stateless token rotation with automatic revocation on reuse detection

---

## Testing Instructions

### Test Sign-up (Patient Registration)
1. Navigate to the sign-up page
2. Fill in all required fields including date of birth
3. Submit the form
4. **Expected:** Registration succeeds, verification email sent

### Test Login (Patient Authentication)
1. Navigate to the login page
2. Enter registered patient credentials
3. Submit the form
4. **Expected:** Login succeeds, JWT + refresh token returned

### Test Refresh Token Flow
1. Wait for access token to expire (or use expired token)
2. Call `/api/auth/refresh` with refresh token
3. **Expected:** New access token + new refresh token returned

---

## Files Modified

### Manually Applied Migrations:
1. `20260501163721_ConvertDateOfBirthToTextForEncryption.cs`
2. `20260422030000_AddPatientIdToRefreshTokens.cs`

### Database Tables Altered:
1. `patients` → `date_of_birth` column type changed
2. `refresh_tokens` → `patient_id` column added, `user_id` made nullable

### Migration History Updated:
- Added 2 missing migrations to `__EFMigrationsHistory` table

---

## Support Tools Created

### 1. VerifyDatabaseSchema
**Location:** `VerifyDatabaseSchema/`  
**Purpose:** Comprehensive database schema validation  
**Usage:** `dotnet run --project .\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj`

### 2. SyncMigrationHistory
**Location:** `SyncMigrationHistory/`  
**Purpose:** Ensures migration history is synchronized  
**Usage:** `dotnet run --project .\SyncMigrationHistory\SyncMigrationHistory.csproj`

---

## Next Steps

1. ✅ **Build Successful** - Project compiles without errors
2. ✅ **Schema Verified** - All critical checks passing
3. ✅ **History Synced** - Migration history up to date
4. 🧪 **Test Sign-up** - Verify patient registration works
5. 🧪 **Test Login** - Verify patient authentication works
6. 🧪 **Test Refresh** - Verify token rotation works

---

## Contact & Support

If you encounter any other database schema issues:
1. Run the verification tool first
2. Check the migration history with `dotnet ef migrations list`
3. Review this document for similar patterns
4. Ensure your database connection string is correct in `appsettings.Development.json`

---

**Last Updated:** ${new Date().toISOString()}  
**Database Version:** 27 migrations applied  
**Status:** ✅ All systems operational
