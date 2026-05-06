#!/usr/bin/env pwsh
<#
.SYNOPSIS
	Database management helper script for Propel IQ platform.

.DESCRIPTION
	Provides convenient commands for common database operations including:
	- Checking migration status
	- Applying migrations
	- Creating new migrations
	- Resetting database (development only)
	- Checking database health

.EXAMPLE
	.\db-manage.ps1 status
	Checks the current database migration status.

.EXAMPLE
	.\db-manage.ps1 migrate
	Applies all pending migrations.

.EXAMPLE
	.\db-manage.ps1 create "AddNewFeature"
	Creates a new migration named "AddNewFeature".

.NOTES
	Author: Propel IQ Development Team
	Requires: .NET SDK, EF Core tools, running API instance for status/migrate
#>

param(
	[Parameter(Mandatory = $true, Position = 0)]
	[ValidateSet("status", "migrate", "create", "remove", "reset", "health", "help")]
	[string]$Command,

	[Parameter(Mandatory = $false, Position = 1)]
	[string]$Name
)

$ErrorActionPreference = "Stop"
$ProjectPath = "Propel.Api.Gateway"
$ApiBaseUrl = "http://localhost:5000"

function Write-Header {
	param([string]$Text)
	Write-Host ""
	Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
	Write-Host " $Text" -ForegroundColor Cyan
	Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
	Write-Host ""
}

function Write-Success {
	param([string]$Text)
	Write-Host "✓ $Text" -ForegroundColor Green
}

function Write-Error {
	param([string]$Text)
	Write-Host "✗ $Text" -ForegroundColor Red
}

function Write-Info {
	param([string]$Text)
	Write-Host "ℹ $Text" -ForegroundColor Blue
}

function Test-ApiRunning {
	try {
		$response = Invoke-WebRequest -Uri "$ApiBaseUrl/health" -Method GET -TimeoutSec 2 -ErrorAction SilentlyContinue
		return $true
	}
	catch {
		return $false
	}
}

function Show-Help {
	Write-Header "Database Management Commands"

	Write-Host "Usage: .\db-manage.ps1 <command> [options]" -ForegroundColor Yellow
	Write-Host ""
	Write-Host "Commands:" -ForegroundColor Yellow
	Write-Host "  status              Check database migration status (requires running API)" -ForegroundColor White
	Write-Host "  migrate             Apply all pending migrations (requires running API)" -ForegroundColor White
	Write-Host "  create <name>       Create a new migration" -ForegroundColor White
	Write-Host "  remove              Remove the last migration (if not applied)" -ForegroundColor White
	Write-Host "  reset               Reset database (DEVELOPMENT ONLY - drops all data)" -ForegroundColor White
	Write-Host "  health              Check database connectivity (requires running API)" -ForegroundColor White
	Write-Host "  help                Show this help message" -ForegroundColor White
	Write-Host ""
	Write-Host "Examples:" -ForegroundColor Yellow
	Write-Host "  .\db-manage.ps1 status" -ForegroundColor Gray
	Write-Host "  .\db-manage.ps1 create AddNewFeature" -ForegroundColor Gray
	Write-Host "  .\db-manage.ps1 migrate" -ForegroundColor Gray
	Write-Host ""
}

function Get-DatabaseStatus {
	Write-Header "Database Migration Status"

	if (-not (Test-ApiRunning)) {
		Write-Error "API is not running at $ApiBaseUrl"
		Write-Info "Start the API first: dotnet run --project $ProjectPath"
		exit 1
	}

	try {
		$response = Invoke-RestMethod -Uri "$ApiBaseUrl/api/database/status" -Method GET

		Write-Host "Connection Status:    " -NoNewline -ForegroundColor Yellow
		if ($response.isConnected) {
			Write-Host "Connected ✓" -ForegroundColor Green
		} else {
			Write-Host "Disconnected ✗" -ForegroundColor Red
		}

		Write-Host "Applied Migrations:   " -NoNewline -ForegroundColor Yellow
		Write-Host "$($response.appliedMigrationsCount)" -ForegroundColor White

		Write-Host "Pending Migrations:   " -NoNewline -ForegroundColor Yellow
		if ($response.pendingMigrationsCount -eq 0) {
			Write-Host "$($response.pendingMigrationsCount) ✓" -ForegroundColor Green
		} else {
			Write-Host "$($response.pendingMigrationsCount) ⚠" -ForegroundColor Yellow
		}

		Write-Host "Last Applied:         " -NoNewline -ForegroundColor Yellow
		Write-Host "$($response.lastAppliedMigration)" -ForegroundColor White

		Write-Host "Status:               " -NoNewline -ForegroundColor Yellow
		Write-Host "$($response.message)" -ForegroundColor White

		if ($response.pendingMigrationsCount -gt 0) {
			Write-Host ""
			Write-Host "Pending Migrations:" -ForegroundColor Yellow
			foreach ($migration in $response.pendingMigrations) {
				Write-Host "  • $migration" -ForegroundColor Gray
			}
			Write-Host ""
			Write-Info "Run '.\db-manage.ps1 migrate' to apply pending migrations"
		}
	}
	catch {
		Write-Error "Failed to get database status: $($_.Exception.Message)"
		exit 1
	}
}

function Invoke-Migration {
	Write-Header "Applying Database Migrations"

	if (-not (Test-ApiRunning)) {
		Write-Error "API is not running at $ApiBaseUrl"
		Write-Info "Start the API first: dotnet run --project $ProjectPath"
		exit 1
	}

	try {
		Write-Info "Triggering migration process..."
		$response = Invoke-RestMethod -Uri "$ApiBaseUrl/api/database/migrate" -Method POST

		if ($response.success) {
			Write-Success "$($response.message)"

			if ($response.migrationsApplied -gt 0) {
				Write-Host ""
				Write-Host "Applied Migrations:" -ForegroundColor Green
				foreach ($migration in $response.appliedMigrations) {
					Write-Host "  ✓ $migration" -ForegroundColor Green
				}
			}
		} else {
			Write-Error "Migration failed: $($response.message)"
			exit 1
		}
	}
	catch {
		Write-Error "Failed to apply migrations: $($_.Exception.Message)"
		exit 1
	}
}

function New-Migration {
	param([string]$MigrationName)

	if ([string]::IsNullOrWhiteSpace($MigrationName)) {
		Write-Error "Migration name is required"
		Write-Info "Usage: .\db-manage.ps1 create <name>"
		exit 1
	}

	Write-Header "Creating New Migration: $MigrationName"

	try {
		Write-Info "Generating migration files..."
		Push-Location $ProjectPath
		dotnet ef migrations add $MigrationName
		Pop-Location

		Write-Success "Migration '$MigrationName' created successfully"
		Write-Info "Migration files are in: $ProjectPath\Migrations\"
		Write-Info "Restart the API to apply this migration automatically"
	}
	catch {
		Pop-Location
		Write-Error "Failed to create migration: $($_.Exception.Message)"
		exit 1
	}
}

function Remove-LastMigration {
	Write-Header "Removing Last Migration"

	Write-Host "⚠ This will remove the most recent migration." -ForegroundColor Yellow
	Write-Host "⚠ Only do this if the migration has NOT been applied to any database." -ForegroundColor Yellow
	Write-Host ""
	$confirmation = Read-Host "Are you sure you want to continue? (yes/no)"

	if ($confirmation -ne "yes") {
		Write-Info "Operation cancelled"
		exit 0
	}

	try {
		Write-Info "Removing last migration..."
		Push-Location $ProjectPath
		dotnet ef migrations remove
		Pop-Location

		Write-Success "Last migration removed successfully"
	}
	catch {
		Pop-Location
		Write-Error "Failed to remove migration: $($_.Exception.Message)"
		Write-Info "If the migration was already applied, you cannot remove it"
		exit 1
	}
}

function Reset-Database {
	Write-Header "Reset Database (DEVELOPMENT ONLY)"

	Write-Host "⚠⚠⚠ DANGER ⚠⚠⚠" -ForegroundColor Red
	Write-Host "This will DROP the entire database and DELETE ALL DATA!" -ForegroundColor Red
	Write-Host "This operation is IRREVERSIBLE!" -ForegroundColor Red
	Write-Host ""
	Write-Host "Only use this in DEVELOPMENT environment!" -ForegroundColor Yellow
	Write-Host ""

	$confirmation = Read-Host "Type 'DELETE ALL DATA' to confirm"

	if ($confirmation -ne "DELETE ALL DATA") {
		Write-Info "Operation cancelled"
		exit 0
	}

	try {
		Write-Info "Dropping database..."
		Push-Location $ProjectPath
		dotnet ef database drop --force
		Pop-Location

		Write-Success "Database dropped successfully"
		Write-Info "Restart the API to recreate the database with all migrations"
	}
	catch {
		Pop-Location
		Write-Error "Failed to reset database: $($_.Exception.Message)"
		exit 1
	}
}

function Test-DatabaseHealth {
	Write-Header "Database Health Check"

	if (-not (Test-ApiRunning)) {
		Write-Error "API is not running at $ApiBaseUrl"
		Write-Info "Start the API first: dotnet run --project $ProjectPath"
		exit 1
	}

	try {
		$response = Invoke-RestMethod -Uri "$ApiBaseUrl/api/database/health" -Method GET

		Write-Host "Status: " -NoNewline -ForegroundColor Yellow
		if ($response.status -eq "Healthy") {
			Write-Host "$($response.status) ✓" -ForegroundColor Green
		} else {
			Write-Host "$($response.status) ✗" -ForegroundColor Red
		}

		Write-Host "Message: " -NoNewline -ForegroundColor Yellow
		Write-Host "$($response.message)" -ForegroundColor White
	}
	catch {
		Write-Error "Health check failed: $($_.Exception.Message)"
		exit 1
	}
}

# Main command routing
switch ($Command) {
	"status" { Get-DatabaseStatus }
	"migrate" { Invoke-Migration }
	"create" { New-Migration -MigrationName $Name }
	"remove" { Remove-LastMigration }
	"reset" { Reset-Database }
	"health" { Test-DatabaseHealth }
	"help" { Show-Help }
}

Write-Host ""
