# Database Schema Sync Script
# This script manually synchronizes the database schema and applies migrations

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Schema Sync Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$connectionString = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"
$env:DATABASE_URL = $connectionString

# PostgreSQL command helper
function Execute-SqlCommand {
    param(
        [string]$Command,
        [string]$Description
    )
    
    Write-Host "$Description..." -ForegroundColor Yellow
    
    # Use psql or fallback to .NET npgsql
    $psqlPath = Get-Command psql -ErrorAction SilentlyContinue
    
    if ($psqlPath) {
        $env:PGPASSWORD = "npg_QAz7gjyI8WHk"
        & psql -h "ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech" -p 5432 -U neondb_owner -d neondb -c $Command
    }
    else {
        Write-Host "psql not found. Please install PostgreSQL client tools or run commands manually." -ForegroundColor Red
        Write-Host "SQL Command: $Command" -ForegroundColor Cyan
        Write-Host ""
    }
}

# Step 1: Check if the constraint exists and drop it if it does
Write-Host "Step 1: Checking for problematic constraints..." -ForegroundColor Yellow
$checkConstraintSql = @"
DO `$`$ 
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'fk_notifications_appointments_appointment_id1' 
        AND table_name = 'notifications'
    ) THEN
        ALTER TABLE notifications DROP CONSTRAINT fk_notifications_appointments_appointment_id1;
        RAISE NOTICE 'Constraint dropped successfully';
    ELSE
        RAISE NOTICE 'Constraint does not exist - skipping';
    END IF;
END `$`$;
"@

Execute-SqlCommand -Command $checkConstraintSql -Description "Removing problematic constraint if exists"

# Step 2: Check if calendar_syncs has the bad foreign key
Write-Host ""
Write-Host "Step 2: Checking calendar_syncs table..." -ForegroundColor Yellow
$checkCalendarSyncsSql = @"
DO `$`$ 
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'fk_calendar_syncs_appointments_appointment_id1' 
        AND table_name = 'calendar_syncs'
    ) THEN
        ALTER TABLE calendar_syncs DROP CONSTRAINT fk_calendar_syncs_appointments_appointment_id1;
        RAISE NOTICE 'Calendar syncs constraint dropped successfully';
    ELSE
        RAISE NOTICE 'Calendar syncs constraint does not exist - skipping';
    END IF;
END `$`$;
"@

Execute-SqlCommand -Command $checkCalendarSyncsSql -Description "Removing calendar_syncs constraint if exists"

# Step 3: Build the project
Write-Host ""
Write-Host "Step 3: Building the project..." -ForegroundColor Yellow
dotnet build Propel.Api.Gateway/Propel.Api.Gateway.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Exiting..." -ForegroundColor Red
    exit 1
}

# Step 4: Apply migrations
Write-Host ""
Write-Host "Step 4: Applying migrations..." -ForegroundColor Yellow
dotnet ef database update --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

if ($LASTEXITCODE -ne 0) {
    Write-Host "Migration failed! See error above." -ForegroundColor Red
    Write-Host ""
    Write-Host "You may need to manually fix the database schema." -ForegroundColor Yellow
    Write-Host "Connection details:" -ForegroundColor Cyan
    Write-Host "  Host: ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech" -ForegroundColor Cyan
    Write-Host "  Port: 5432" -ForegroundColor Cyan
    Write-Host "  Database: neondb" -ForegroundColor Cyan
    Write-Host "  Username: neondb_owner" -ForegroundColor Cyan
    Write-Host "  Password: npg_QAz7gjyI8WHk" -ForegroundColor Cyan
    exit 1
}

Write-Host ""
Write-Host "Step 5: Verifying migrations..." -ForegroundColor Yellow
dotnet ef migrations list --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Database schema sync completed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review migration history above" -ForegroundColor White
Write-Host "2. Check if PHI columns need fixing (fix_phi_columns.sql)" -ForegroundColor White
Write-Host "3. Verify refresh_tokens table has patient_id column" -ForegroundColor White
