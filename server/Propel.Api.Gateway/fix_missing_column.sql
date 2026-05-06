-- Add missing pending_alerts_json column to patients table
-- This column was defined in migration 20260422140000_AddPatientPendingAlerts but was not applied

ALTER TABLE patients 
ADD COLUMN IF NOT EXISTS pending_alerts_json jsonb NULL;

-- Verify the column was added
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'patients' 
  AND column_name = 'pending_alerts_json';

-- Insert the migration record to keep history in sync
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260422140000_AddPatientPendingAlerts', '9.0.15')
ON CONFLICT (migration_id) DO NOTHING;
