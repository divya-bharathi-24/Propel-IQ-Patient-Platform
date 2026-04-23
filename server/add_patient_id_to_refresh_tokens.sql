-- Migration: Add PatientId support to RefreshToken table
-- This script manually applies the changes from migration 20260422030000_AddPatientIdToRefreshTokens

-- Step 1: Drop existing composite index
DROP INDEX IF EXISTS ix_refresh_tokens_user_id_family_id;

-- Step 2: Drop existing FK constraint
ALTER TABLE refresh_tokens 
DROP CONSTRAINT IF EXISTS fk_refresh_tokens_users_user_id;

-- Step 3: Make user_id nullable
ALTER TABLE refresh_tokens 
ALTER COLUMN user_id DROP NOT NULL;

-- Step 4: Add patient_id column (nullable)
ALTER TABLE refresh_tokens 
ADD COLUMN IF NOT EXISTS patient_id uuid NULL;

-- Step 5: Re-create FK constraint to users table
ALTER TABLE refresh_tokens
ADD CONSTRAINT fk_refresh_tokens_users_user_id
FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;

-- Step 6: Create partial composite index on (user_id, family_id) for staff/admin tokens
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id_family_id 
ON refresh_tokens (user_id, family_id)
WHERE user_id IS NOT NULL;

-- Step 7: Create partial composite index on (patient_id, family_id) for patient tokens
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_patient_id_family_id 
ON refresh_tokens (patient_id, family_id)
WHERE patient_id IS NOT NULL;

-- Step 8: Create index on patient_id for FK performance
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_patient_id 
ON refresh_tokens (patient_id);

-- Step 9: Create FK constraint to patients table
ALTER TABLE refresh_tokens
ADD CONSTRAINT fk_refresh_tokens_patients_patient_id
FOREIGN KEY (patient_id) REFERENCES patients(id) ON DELETE CASCADE;

-- Step 10: Add CHECK constraint: exactly one of patient_id or user_id must be non-null
ALTER TABLE refresh_tokens
ADD CONSTRAINT ck_refresh_tokens_patient_or_user
CHECK (
    (patient_id IS NOT NULL AND user_id IS NULL) OR
    (patient_id IS NULL AND user_id IS NOT NULL)
);

-- Step 11: Insert migration history record
INSERT INTO "__EFMigrationsHistory" ("migration_id", "product_version")
VALUES ('20260422030000_AddPatientIdToRefreshTokens', '9.0.15')
ON CONFLICT ("migration_id") DO NOTHING;
