#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies pending EF Core migrations to the Propel database.

.DESCRIPTION
    This script applies all pending Entity Framework Core migrations to bring
    the PostgreSQL database schema in sync with the current EF Core model.

    Specifically, this will apply the AddPatientIdToRefreshTokens migration
    that adds patient authentication support to the refresh_tokens table.

.EXAMPLE
    .\apply-migrations.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Propel Database Migration Script" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Change to the script directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

Write-Host "[1/3] Checking for dotnet CLI..." -ForegroundColor Yellow
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Please install .NET 10 SDK." -ForegroundColor Red
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "      Found: .NET SDK $dotnetVersion" -ForegroundColor Green
Write-Host ""

Write-Host "[2/3] Checking for pending migrations..." -ForegroundColor Yellow
try {
    $pendingMigrations = dotnet ef migrations list --project server/Propel.Api.Gateway --no-build --json 2>&1 | ConvertFrom-Json
    $pending = $pendingMigrations | Where-Object { -not $_.applied }

    if ($pending) {
        Write-Host "      Found $($pending.Count) pending migration(s):" -ForegroundColor Yellow
        foreach ($migration in $pending) {
            Write-Host "      - $($migration.name)" -ForegroundColor White
        }
    } else {
        Write-Host "      No pending migrations found. Database is up to date." -ForegroundColor Green
        exit 0
    }
} catch {
    Write-Host "      Unable to check migrations. Proceeding with update..." -ForegroundColor Yellow
}
Write-Host ""

Write-Host "[3/3] Applying migrations to database..." -ForegroundColor Yellow
Write-Host "      Project: Propel.Api.Gateway" -ForegroundColor White
Write-Host ""

try {
    # Apply migrations
    dotnet ef database update --project server/Propel.Api.Gateway --verbose

    Write-Host ""
    Write-Host "================================================" -ForegroundColor Green
    Write-Host "  SUCCESS: Migrations applied successfully!" -ForegroundColor Green
    Write-Host "================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "The refresh_tokens table now includes:" -ForegroundColor Green
    Write-Host "  - patient_id column (nullable)" -ForegroundColor White
    Write-Host "  - user_id column (now nullable)" -ForegroundColor White
    Write-Host "  - CHECK constraint ensuring exactly one is non-null" -ForegroundColor White
    Write-Host "  - Foreign keys and indexes for patient authentication" -ForegroundColor White
    Write-Host ""
    Write-Host "You can now restart your application." -ForegroundColor Cyan

} catch {
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Red
    Write-Host "  ERROR: Migration failed!" -ForegroundColor Red
    Write-Host "================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting steps:" -ForegroundColor Yellow
    Write-Host "1. Ensure PostgreSQL is running and accessible" -ForegroundColor White
    Write-Host "2. Check your connection string in appsettings.json" -ForegroundColor White
    Write-Host "3. Verify database credentials are correct" -ForegroundColor White
    Write-Host "4. Check Docker containers if using docker-compose:" -ForegroundColor White
    Write-Host "   docker-compose ps" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
