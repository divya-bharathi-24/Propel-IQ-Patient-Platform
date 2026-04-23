# Fix for RefreshToken PatientId Column Error

## Problem
The error `column "patient_id" of relation "refresh_tokens" does not exist` occurred because the migration to add `PatientId` support to the `RefreshToken` entity was placed in the wrong directory and was never applied to the database.

## Solution
I've fixed this by:

1. **Created the proper migration file**: `Propel.Api.Gateway/Migrations/20260422030000_AddPatientIdToRefreshTokens.cs`
2. **Removed the incorrectly placed file**: `Propel.Api.Gateway/Data/Migrations/AddPatientIdToRefreshTokens.cs`
3. **Created a SQL script** for manual application: `add_patient_id_to_refresh_tokens.sql`

## How to Apply the Migration

### Option 1: Using Entity Framework (Recommended after stopping the app)
1. Stop the running application in Visual Studio
2. Open a terminal in the `server` directory
3. Run:
   ```powershell
   cd Propel.Api.Gateway
   dotnet ef database update
   ```

### Option 2: Using the SQL Script (While app is running)
Since the app is currently running and files are locked, you can apply the migration manually:

#### Using pgAdmin:
1. Open pgAdmin
2. Connect to the `propeliq_dev` database (localhost:5434)
3. Open Query Tool
4. Copy and paste the contents of `add_patient_id_to_refresh_tokens.sql`
5. Execute the query (F5)

#### Using psql (if available):
```powershell
$env:PGPASSWORD="Jothis@10"
psql -h 127.0.0.1 -p 5434 -U postgres -d propeliq_dev -f add_patient_id_to_refresh_tokens.sql
```

#### Using DBeaver or another PostgreSQL client:
1. Connect to: `Server=127.0.0.1;Port=5434;Database=propeliq_dev;User=postgres;Password=Jothis@10`
2. Open the SQL script `add_patient_id_to_refresh_tokens.sql`
3. Execute it

## What Changed

The migration adds `PatientId` support to the `RefreshToken` table, allowing refresh tokens to be used for both Patient and User (Staff/Admin) authentication:

- Made `user_id` nullable
- Added `patient_id` nullable column with FK to patients table
- Added CHECK constraint ensuring exactly one of `patient_id` or `user_id` is non-null
- Added composite index on `(patient_id, family_id)` for patient token family revocation
- Updated existing composite index on `(user_id, family_id)` to use partial index filtering

## After Applying
Once the migration is applied:
1. Restart the application
2. Try the login again - it should work correctly
3. The refresh token will be stored with the `PatientId` populated

## Verification
To verify the migration was applied successfully, run this SQL:
```sql
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'refresh_tokens' 
ORDER BY ordinal_position;
```

You should see both `user_id` and `patient_id` columns, both nullable.
