-- Fix PHI column types for encrypted storage
-- Run this SQL script manually in your PostgreSQL database

-- Update date_of_birth from DATE to TEXT
ALTER TABLE patients 
ALTER COLUMN date_of_birth TYPE text 
USING date_of_birth::text;

-- Update name from VARCHAR(200) to TEXT  
ALTER TABLE patients 
ALTER COLUMN name TYPE text;

-- Update phone from VARCHAR(30) to TEXT
ALTER TABLE patients 
ALTER COLUMN phone TYPE text;

-- Verify the changes
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name = 'patients'
AND column_name IN ('date_of_birth', 'name', 'phone');
