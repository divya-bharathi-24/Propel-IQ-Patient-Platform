# Database Schema Verification Script
# This script checks if manual SQL scripts need to be applied

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Schema Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$connectionString = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"

Write-Host "This script will check if the following SQL scripts need to be applied:" -ForegroundColor Yellow
Write-Host "  1. fix_phi_columns.sql - Convert PHI columns to TEXT type" -ForegroundColor White
Write-Host "  2. add_patient_id_to_refresh_tokens.sql - Add patient_id to refresh_tokens" -ForegroundColor White
Write-Host ""

# Check for psql
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue

if (-not $psqlPath) {
    Write-Host "? PostgreSQL client (psql) not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "To verify the database schema, you can:" -ForegroundColor Yellow
    Write-Host "  1. Install PostgreSQL client tools" -ForegroundColor White
    Write-Host "  2. Use pgAdmin or another PostgreSQL GUI tool" -ForegroundColor White
    Write-Host "  3. Use an online PostgreSQL client" -ForegroundColor White
    Write-Host ""
    Write-Host "Connection details:" -ForegroundColor Cyan
    Write-Host "  Host: ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech" -ForegroundColor White
    Write-Host "  Port: 5432" -ForegroundColor White
    Write-Host "  Database: neondb" -ForegroundColor White
    Write-Host "  Username: neondb_owner" -ForegroundColor White
    Write-Host "  Password: npg_QAz7gjyI8WHk" -ForegroundColor White
    Write-Host ""
    Write-Host "Manual verification queries:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "-- Check PHI column types" -ForegroundColor Gray
    Write-Host "SELECT column_name, data_type, character_maximum_length" -ForegroundColor White
    Write-Host "FROM information_schema.columns" -ForegroundColor White
    Write-Host "WHERE table_name = 'patients'" -ForegroundColor White
    Write-Host "AND column_name IN ('date_of_birth', 'name', 'phone');" -ForegroundColor White
    Write-Host ""
    Write-Host "-- Check if refresh_tokens has patient_id column" -ForegroundColor Gray
    Write-Host "SELECT column_name FROM information_schema.columns" -ForegroundColor White
    Write-Host "WHERE table_name = 'refresh_tokens' AND column_name = 'patient_id';" -ForegroundColor White
    Write-Host ""
    exit 0
}

Write-Host "? PostgreSQL client found. Connecting to database..." -ForegroundColor Green
Write-Host ""

$env:PGPASSWORD = "npg_QAz7gjyI8WHk"

# Check 1: PHI columns
Write-Host "Check 1: Verifying PHI column types..." -ForegroundColor Yellow
$phiColumnsQuery = @"
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name = 'patients'
AND column_name IN ('date_of_birth', 'name', 'phone');
"@

Write-Host "Executing query..." -ForegroundColor Gray
$phiResult = & psql -h "ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech" -p 5432 -U neondb_owner -d neondb -c $phiColumnsQuery -t -A -F "|"

Write-Host "Results:" -ForegroundColor Cyan
$phiResult | ForEach-Object { Write-Host "  $_" -ForegroundColor White }

$needsPhiFix = $false
foreach ($line in $phiResult) {
    if ($line -match "varchar|date\|" -and $line -notmatch "text") {
        $needsPhiFix = $true
        break
    }
}

if ($needsPhiFix) {
    Write-Host "??  PHI columns are NOT text type. fix_phi_columns.sql needs to be applied." -ForegroundColor Red
} else {
    Write-Host "? PHI columns are already text type. No action needed." -ForegroundColor Green
}

Write-Host ""

# Check 2: patient_id in refresh_tokens
Write-Host "Check 2: Verifying patient_id column in refresh_tokens..." -ForegroundColor Yellow
$patientIdQuery = @"
SELECT column_name FROM information_schema.columns 
WHERE table_name = 'refresh_tokens' AND column_name = 'patient_id';
"@

Write-Host "Executing query..." -ForegroundColor Gray
$patientIdResult = & psql -h "ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech" -p 5432 -U neondb_owner -d neondb -c $patientIdQuery -t -A

if ([string]::IsNullOrWhiteSpace($patientIdResult)) {
    Write-Host "? patient_id column does NOT exist. add_patient_id_to_refresh_tokens.sql needs to be applied." -ForegroundColor Red
} else {
    Write-Host "? patient_id column exists. No action needed." -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Verification Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Summary
Write-Host "Summary:" -ForegroundColor Yellow
if ($needsPhiFix) {
    Write-Host "  ? Run: fix_phi_columns.sql" -ForegroundColor Red
} else {
    Write-Host "  ? fix_phi_columns.sql: Not needed" -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($patientIdResult)) {
    Write-Host "  ? Run: add_patient_id_to_refresh_tokens.sql" -ForegroundColor Red
} else {
    Write-Host "  ? add_patient_id_to_refresh_tokens.sql: Not needed" -ForegroundColor Green
}

Write-Host ""

if ($needsPhiFix -or [string]::IsNullOrWhiteSpace($patientIdResult)) {
    Write-Host "To apply SQL scripts manually:" -ForegroundColor Yellow
    Write-Host "  psql -h ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech -p 5432 -U neondb_owner -d neondb -f <script_name>.sql" -ForegroundColor White
    Write-Host ""
} else {
    Write-Host "?? Database schema is up to date! No manual scripts needed." -ForegroundColor Green
    Write-Host ""
}
