-- Add pending_alerts_json column to patients table if it doesn't exist
ALTER TABLE patients ADD COLUMN IF NOT EXISTS pending_alerts_json jsonb;
