# RefreshToken Foreign Key Constraint Fix

## Problem Summary

**Exception**: `Npgsql.PostgresException:23503: insert or update on table "refresh_tokens" violates foreign key constraint "fk_refresh_tokens_users_user_id"`

**Root Cause**: The `RefreshToken` entity was configured with a foreign key constraint that only referenced the `users` table. However, the application has **two separate authentication entities**:
- `Patient` - for patient accounts (authenticated via email/password)
- `User` - for staff/admin accounts

When a patient logged in via `LoginCommandHandler`, the code attempted to create a `RefreshToken` with `UserId = patient.Id`, but that patient ID doesn't exist in the `users` table, causing the foreign key constraint violation.

## Solution Implemented

**Option 1: Dual Foreign Key Support** - Modified `RefreshToken` to support both Patient and User authentication.

### Changes Made

#### 1. **RefreshToken Entity** (`Propel.Domain\Entities\RefreshToken.cs`)
- Changed `UserId` from `Guid` to `Guid?` (nullable)
- Added `PatientId` property as `Guid?` (nullable)
- Updated documentation to reflect dual authentication support
- Exactly one of `PatientId` or `UserId` must be non-null (enforced via CHECK constraint)

#### 2. **RefreshTokenConfiguration** (`Propel.Api.Gateway\Data\Configurations\RefreshTokenConfiguration.cs`)
- Made both `PatientId` and `UserId` nullable
- Added CHECK constraint: `(patient_id IS NOT NULL AND user_id IS NULL) OR (patient_id IS NULL AND user_id IS NOT NULL)`
- Added foreign key constraint to `patients` table with CASCADE delete
- Created partial composite index on `(patient_id, family_id)` for patient tokens
- Updated existing `(user_id, family_id)` index to use partial index filter

#### 3. **LoginCommandHandler** (`Propel.Modules.Auth\Handlers\LoginCommandHandler.cs`)
- Updated to set `PatientId = patient.Id` instead of `UserId = patient.Id`
- Explicitly set `UserId = null` for patient logins

#### 4. **RefreshTokenCommandHandler** (`Propel.Modules.Auth\Handlers\RefreshTokenCommandHandler.cs`)
- Added logic to determine the correct `userId` from either `PatientId` or `UserId`
- Determine role based on which FK is set (`"Patient"` if `PatientId` is set, otherwise `"Staff"`)
- Updated token rotation to preserve both `PatientId` and `UserId` in the new token
- Updated audit logging to use correct `PatientId` value

#### 5. **Database Migration** (`Propel.Api.Gateway\Data\Migrations\AddPatientIdToRefreshTokens.cs`)
Created migration that:
- Makes `user_id` nullable
- Adds `patient_id` column (nullable)
- Adds CHECK constraint to ensure exactly one ID is set
- Creates partial composite indexes on both `(user_id, family_id)` and `(patient_id, family_id)`
- Adds foreign key to `patients` table with CASCADE delete
- Includes proper rollback logic in `Down()` method

## Migration Steps

1. **Apply the migration**:
   ```bash
   dotnet ef migrations add AddPatientIdToRefreshTokens --project Propel.Api.Gateway
   dotnet ef database update --project Propel.Api.Gateway
   ```

2. **Verify the changes**:
   - Check that the `refresh_tokens` table now has both `patient_id` and `user_id` columns (both nullable)
   - Verify CHECK constraint `ck_refresh_tokens_patient_or_user` exists
   - Verify foreign key `fk_refresh_tokens_patients_patient_id` exists
   - Verify partial indexes exist

3. **Test patient login**:
   - Patients should now be able to log in successfully
   - Refresh tokens should be created with `patient_id` set and `user_id` null
   - Token refresh should work correctly for patients

4. **Future: Test staff/admin login** (when implemented):
   - Staff/admin users should log in with `user_id` set and `patient_id` null
   - Token refresh should work correctly for staff/admin

## Database Schema Changes

### Before
```sql
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(512) NOT NULL,
    family_id UUID NOT NULL,
    device_id VARCHAR(255) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ
);
```

### After
```sql
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY,
    patient_id UUID REFERENCES patients(id) ON DELETE CASCADE,
    user_id UUID REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(512) NOT NULL,
    family_id UUID NOT NULL,
    device_id VARCHAR(255) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    
    CONSTRAINT ck_refresh_tokens_patient_or_user
        CHECK ((patient_id IS NOT NULL AND user_id IS NULL) OR 
               (patient_id IS NULL AND user_id IS NOT NULL))
);
```

## Security Considerations

- ? Maintains OWASP token rotation pattern
- ? SHA-256 token hashing still enforced
- ? Family-based reuse detection works for both patient and staff tokens
- ? Cascade delete ensures orphaned tokens are cleaned up when accounts are deleted
- ? CHECK constraint prevents invalid state (both or neither ID set)
- ? Partial indexes optimize queries for both authentication types

## Testing Checklist

- [ ] Patient login creates refresh token with `patient_id` set
- [ ] Patient token refresh works correctly
- [ ] Patient token family revocation works
- [ ] Patient logout revokes tokens correctly
- [ ] Staff/admin login creates refresh token with `user_id` set (when staff login is implemented)
- [ ] Cannot create refresh token with both IDs null (CHECK constraint)
- [ ] Cannot create refresh token with both IDs non-null (CHECK constraint)
- [ ] Cascade delete removes patient refresh tokens when patient is deleted
- [ ] Cascade delete removes user refresh tokens when user is deleted
