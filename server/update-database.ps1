# Database Update Script
# This script updates the connection string, applies migrations, and runs SQL scripts

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Database Update and Migration Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Set the connection string
$env:DATABASE_URL = "Host=ep-divine-mode-amg8nhin.c-5.us-east-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_QAz7gjyI8WHk;SSL Mode=Require;Trust Server Certificate=true;"

Write-Host "Step 1: Checking EF Core Tools..." -ForegroundColor Yellow
dotnet ef --version
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing EF Core Tools..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-ef
}

Write-Host ""
Write-Host "Step 2: Building the project..." -ForegroundColor Yellow
dotnet build Propel.Api.Gateway/Propel.Api.Gateway.csproj
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed! Exiting..." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Step 3: Checking current migration status..." -ForegroundColor Yellow
dotnet ef database update --list --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

Write-Host ""
Write-Host "Step 4: Applying all pending migrations..." -ForegroundColor Yellow
dotnet ef database update --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

if ($LASTEXITCODE -ne 0) {
    Write-Host "Migration failed! Check the error above." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Step 5: Verifying migrations were applied..." -ForegroundColor Yellow
dotnet ef migrations list --project Propel.Api.Gateway --startup-project Propel.Api.Gateway

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Database migrations completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Connection String: $env:DATABASE_URL" -ForegroundColor Cyan
Write-Host ""
Write-Host "Note: SQL scripts (fix_phi_columns.sql, add_patient_id_to_refresh_tokens.sql) may have" -ForegroundColor Yellow
Write-Host "already been applied through migrations. Review the migration history before running them manually." -ForegroundColor Yellow
