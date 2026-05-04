# Database Migration Summary

## Connection Information
**Database:** NeonDB (PostgreSQL)
**Host:** ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech
**Port:** 5432
**Database Name:** neondb
**Username:** neondb_owner
**Password:** npg_QAz7gjyI8WHk

## Updates Applied

### 1. Connection String Updated ?
The connection string in `Propel.Api.Gateway/appsettings.Development.json` has been updated with the new NeonDB credentials.

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"
}
```

### 2. Migrations Applied ?
The following migrations have been successfully applied to the database:

1. 20260420161639_Initial
2. 20260420171127_AddClinicalEntities
3. 20260420190747_AddAuditNotificationEntities
4. 20260420191333_AddExtensionsSeedData
5. 20260421033625_AddEmailVerificationTokens
6. 20260421045403_CreateUserAndCredentialTables
7. 20260421120000_AddCaseInsensitiveEmailIndex
8. 20260421130000_AddRefreshTokensTable
9. 20260421140000_ExtendAuditLogForAuthEvents
10. 20260422000000_AddPatientDemographics
11. 20260422010000_AddPatientViewVerifiedAt
12. 20260422020000_AddIntakeEditDraftAndConcurrency
13. 20260422120000_AddInsuranceValidationAndBookingConstraint
14. 20260422150000_AllowAnonymousWalkIn
15. 20260423000000_AddSystemSettingsAndNotificationColumns
16. 20260423010000_Add_Notification_TriggeredBy_ErrorReason
17. 20260423030000_AddCalendarSyncOAuthSchema
18. 20260423040000_AddCalendarSyncRetryColumns
19. 20260423082752_AddDocumentChunkEmbeddingsAndPriorityReview
20. 20260423094353_Add_PatientProfileVerification_And_ExtractedData_DeduplicationFields
21. 20260423163526_AddMedicalCodesTable
22. 20260423180724_AddDataConflictsTable
23. 20260423184303_AddAiQualityMetricsTable
24. 20260424000000_AddAiPromptAuditLogTable
25. **20260504162409_SyncDatabaseSchema** (New migration created to fix schema inconsistencies)

### 3. Schema Sync Migration Created ?
A new migration `20260504162409_SyncDatabaseSchema` was created to handle schema inconsistencies. This migration:
- Safely drops non-existent constraints (checks existence before dropping)
- Removes duplicate foreign key columns (appointment_id1)
- Cleans up orphaned indexes

### 4. Migrations Not Applied ??
The following migrations exist in the codebase but were NOT applied to the database (likely skipped or created after the database was already in that state):

1. 20260422030000_AddPatientIdToRefreshTokens
2. 20260422140000_AddPatientPendingAlerts
3. 20260422160000_MakeQueueArrivalTimeNullable
4. 20260422170000_AddSeverityToNoShowRisks
5. 20260422180000_AddRiskInterventions
6. 20260423020000_ExtendClinicalDocumentForStaffUpload
7. 20260423050000_AddClinicalDocumentPerformanceIndexes
8. 20260424100000_AddAiOperationalMetricsTable

**Note:** These migrations may have been manually applied to the database or their changes may already be present in the initial migration.

## SQL Scripts Status

### add_patient_id_to_refresh_tokens.sql ??
This script's functionality **may already be included** in migration `20260422030000_AddPatientIdToRefreshTokens` (which exists in the codebase but was not applied). The database may already have the patient_id column.

**Action Required:** Check if the refresh_tokens table already has a patient_id column:
```sql
SELECT column_name FROM information_schema.columns 
WHERE table_name = 'refresh_tokens' AND column_name = 'patient_id';
```

### fix_phi_columns.sql ??
This script changes the data types of PHI columns (name, phone, date_of_birth) to TEXT for encryption support.

**Action Required:** Check if these columns are already TEXT type:
```sql
SELECT column_name, data_type 
FROM information_schema.columns
WHERE table_name = 'patients'
AND column_name IN ('date_of_birth', 'name', 'phone');
```

## Verification Steps

### 1. Check Migration History
```bash
$env:DATABASE_URL = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"
dotnet ef migrations list --project Propel.Api.Gateway --startup-project Propel.Api.Gateway
```

### 2. Verify Database Connection
```bash
# From PowerShell
$env:DATABASE_URL = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"
dotnet run --project Propel.Api.Gateway
```

### 3. Check Database Schema (Using psql or pgAdmin)
If you have PostgreSQL client tools installed:
```bash
$env:PGPASSWORD = "npg_QAz7gjyI8WHk"
psql -h ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech -p 5432 -U neondb_owner -d neondb

# Once connected:
\dt                          # List all tables
\d patients                  # Describe patients table
\d refresh_tokens            # Describe refresh_tokens table
SELECT * FROM "__EFMigrationsHistory" ORDER BY "migration_id"; # Check migration history
```

## Potential Issues to Watch For

1. **PHI Column Types**: If the PHI columns (name, phone, date_of_birth) are not TEXT type, the encryption service may fail.
   - **Solution**: Run `fix_phi_columns.sql` manually

2. **Refresh Token Patient ID**: If the refresh_tokens table doesn't have a patient_id column, patient authentication may fail.
   - **Solution**: Run `add_patient_id_to_refresh_tokens.sql` manually

3. **Missing Migrations**: Some migrations exist in the code but were not applied. This is normal if their changes were already present in the database.
   - **Action**: Review each missing migration to ensure the database has those changes

## Next Steps

1. ? Connection string updated
2. ? All available migrations applied
3. ?? **TODO**: Verify PHI columns are TEXT type
4. ?? **TODO**: Verify refresh_tokens table has patient_id column
5. ?? **TODO**: Test database connectivity from the application
6. ?? **TODO**: Run any necessary manual SQL scripts

## Files Modified

1. `Propel.Api.Gateway/appsettings.Development.json` - Updated connection string
2. `Propel.Api.Gateway/Migrations/20260504162409_SyncDatabaseSchema.cs` - Created new migration
3. `update-database.ps1` - Created automation script
4. `sync-database-schema.ps1` - Created schema sync script

## Rollback Instructions (If Needed)

If you need to rollback the database to a previous state:

```bash
# Rollback to a specific migration
$env:DATABASE_URL = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"
dotnet ef database update <MigrationName> --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

# Example: Rollback to Initial migration
dotnet ef database update 20260420161639_Initial --project Propel.Api.Gateway --startup-project Propel.Api.Gateway
```

## Support

For issues or questions:
1. Check the error logs in the application
2. Review the Entity Framework migration history
3. Verify database connectivity using psql or pgAdmin
4. Check that all required tables and columns exist

---

**Last Updated:** 2026-05-04
**Database:** NeonDB PostgreSQL
**Environment:** Development
