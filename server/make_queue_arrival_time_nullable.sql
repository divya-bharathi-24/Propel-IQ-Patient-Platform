-- Migration: MakeQueueArrivalTimeNullable (US_027 — Same-Day Queue Arrived Status Marking)
-- Applied manually via apply_migration.ps1 pattern (EF Core tools incompatible with project design-time context).
--
-- Changes:
--   1. Makes arrival_time nullable on queue_entries.
--      Allows MarkArrived to set the timestamp and RevertArrived to clear it (set to NULL).
--
-- Safe to run multiple times (idempotent via IF NOT EXISTS guard).

DO $$
BEGIN
    -- Make arrival_time nullable if it is currently NOT NULL
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'queue_entries'
          AND column_name = 'arrival_time'
          AND is_nullable = 'NO'
    ) THEN
        ALTER TABLE queue_entries ALTER COLUMN arrival_time DROP NOT NULL;
        RAISE NOTICE 'arrival_time column made nullable on queue_entries.';
    ELSE
        RAISE NOTICE 'arrival_time is already nullable on queue_entries — no change needed.';
    END IF;
END $$;
