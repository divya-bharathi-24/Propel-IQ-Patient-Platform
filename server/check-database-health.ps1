# Quick Database Health Check Script
# Run this anytime you suspect database schema issues

Write-Host "=== Propel-IQ Database Health Check ===" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the server directory
$currentPath = Get-Location
if ($currentPath.Path -notlike "*\server") {
	Write-Host "⚠️  Not in server directory. Changing to server..." -ForegroundColor Yellow
	Set-Location "D:\Siva Propel\Propel Latest\Propel-IQ-Patient-Platform\server"
}

Write-Host "1️⃣  Checking solution builds..." -ForegroundColor Yellow
$buildResult = dotnet build --verbosity quiet --nologo
if ($LASTEXITCODE -eq 0) {
	Write-Host "   ✅ Build successful" -ForegroundColor Green
} else {
	Write-Host "   ❌ Build failed" -ForegroundColor Red
	exit 1
}

Write-Host ""
Write-Host "2️⃣  Verifying database schema..." -ForegroundColor Yellow
if (Test-Path ".\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj") {
	dotnet run --project .\VerifyDatabaseSchema\VerifyDatabaseSchema.csproj
} else {
	Write-Host "   ⚠️  Schema verification tool not found" -ForegroundColor Yellow
	Write-Host "   Run: dotnet ef database update --project Propel.Api.Gateway" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "3️⃣  Checking migration status..." -ForegroundColor Yellow
Set-Location "Propel.Api.Gateway"
$migrations = dotnet ef migrations list --no-build 2>&1
if ($LASTEXITCODE -eq 0) {
	Write-Host "   ✅ Migrations accessible" -ForegroundColor Green
	$pendingCount = ($migrations | Select-String "pending" | Measure-Object).Count
	if ($pendingCount -gt 0) {
		Write-Host "   ⚠️  $pendingCount pending migrations found" -ForegroundColor Yellow
		Write-Host "   Run: dotnet ef database update" -ForegroundColor Cyan
	} else {
		Write-Host "   ✅ All migrations applied" -ForegroundColor Green
	}
} else {
	Write-Host "   ❌ Cannot check migrations" -ForegroundColor Red
}
Set-Location ..

Write-Host ""
Write-Host "=== Health Check Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "For detailed information, see: DATABASE_SCHEMA_FIX_SUMMARY.md" -ForegroundColor Gray
