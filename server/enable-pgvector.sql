-- Enable pgvector Extension for PostgreSQL
-- Required for AI RAG features (US_040, task_002)
--
-- IMPORTANT: This must be run by a PostgreSQL superuser or database owner
-- 
-- Connection details from appsettings.Development.json:
-- Server: 127.0.0.1
-- Port: 5434
-- Database: propeliq_dev
-- User: postgres
-- Password: Jothis@10
--
-- To execute this SQL file:
-- Method 1 (psql command line):
--   psql -h 127.0.0.1 -p 5434 -U postgres -d propeliq_dev -f enable-pgvector.sql
--
-- Method 2 (pgAdmin or any PostgreSQL client):
--   Connect to the database and run this script
--
-- Method 3 (Docker if running PostgreSQL in container):
--   docker exec -i <postgres-container-name> psql -U postgres -d propeliq_dev < enable-pgvector.sql

-- Enable the pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;

-- Verify the extension is installed
SELECT 
    extname AS "Extension Name",
    extversion AS "Version",
    extnamespace::regnamespace AS "Schema"
FROM pg_extension 
WHERE extname = 'vector';

-- If the above query returns a row, the extension is successfully installed
-- Expected output:
-- Extension Name | Version | Schema
-- vector         | 0.5.1   | public

COMMENT ON EXTENSION vector IS 'pgvector extension for vector similarity search - required for AI RAG features (US_040)';
