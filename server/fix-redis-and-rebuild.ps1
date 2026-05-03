# Fix StackExchange.Redis Version Mismatch and Restart
# Run this script after stopping the debugger

Write-Host "=== StackExchange.Redis Version Fix - Rebuild Script ===" -ForegroundColor Cyan
Write-Host ""

# Navigate to server directory
Set-Location -Path "D:\Propel_IQ\Propel-IQ-Patient-Platform\server"

# Step 1: Clean all projects
Write-Host "[1/5] Cleaning solution..." -ForegroundColor Yellow
dotnet clean --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Clean failed" -ForegroundColor Red
    exit 1
}
Write-Host "? Clean complete" -ForegroundColor Green
Write-Host ""

# Step 2: Restore packages (will pull StackExchange.Redis 2.8.*)
Write-Host "[2/5] Restoring packages..." -ForegroundColor Yellow
dotnet restore --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Restore failed" -ForegroundColor Red
    exit 1
}
Write-Host "? Packages restored" -ForegroundColor Green
Write-Host ""

# Step 3: Build solution
Write-Host "[3/5] Building solution..." -ForegroundColor Yellow
dotnet build --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Build failed - see errors above" -ForegroundColor Red
    exit 1
}
Write-Host "? Build successful" -ForegroundColor Green
Write-Host ""

# Step 4: Verify StackExchange.Redis version
Write-Host "[4/5] Verifying StackExchange.Redis version..." -ForegroundColor Yellow
$dllPath = "Propel.Api.Gateway\bin\Debug\net10.0\StackExchange.Redis.dll"
if (Test-Path $dllPath) {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
    Write-Host "? StackExchange.Redis version: $version" -ForegroundColor Green
    
    if ($version -like "2.8.*") {
        Write-Host "? Version is correct (2.8.x)" -ForegroundColor Green
    } else {
        Write-Host "? Warning: Expected version 2.8.x but got $version" -ForegroundColor Yellow
    }
} else {
    Write-Host "? DLL not found at $dllPath" -ForegroundColor Yellow
}
Write-Host ""

# Step 5: Ready to run
Write-Host "[5/5] Ready to start application" -ForegroundColor Yellow
Write-Host ""
Write-Host "=== FIX COMPLETE ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Press F5 to restart debugging in Visual Studio"
Write-Host "  2. Or run: dotnet run --project Propel.Api.Gateway"
Write-Host ""
Write-Host "The TypeLoadException should now be resolved." -ForegroundColor Green
