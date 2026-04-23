# Propel IQ - Development Environment Startup Script
# This script starts both the .NET API and Angular frontend with proxy configuration

Write-Host "?? Starting Propel IQ Development Environment..." -ForegroundColor Cyan
Write-Host ""

# Check if running in the correct directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverPath = Join-Path $scriptPath "server\Propel.Api.Gateway"
$appPath = Join-Path $scriptPath "app"

if (-not (Test-Path $serverPath)) {
    Write-Host "? Error: Server path not found at $serverPath" -ForegroundColor Red
    Write-Host "Please run this script from the repository root directory." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $appPath)) {
    Write-Host "? Error: App path not found at $appPath" -ForegroundColor Red
    Write-Host "Please run this script from the repository root directory." -ForegroundColor Yellow
    exit 1
}

# Start .NET API in a new PowerShell window
Write-Host "?? Starting .NET API Gateway..." -ForegroundColor Green
Start-Process pwsh -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$serverPath'; Write-Host '?? .NET API Gateway' -ForegroundColor Cyan; dotnet run --launch-profile https"
) -WindowStyle Normal

Write-Host "   ? API will be available at: https://localhost:5001" -ForegroundColor Gray

# Wait for API to start (give it time to initialize)
Write-Host ""
Write-Host "? Waiting 8 seconds for API to initialize..." -ForegroundColor Yellow
Start-Sleep -Seconds 8

# Start Angular with Proxy in a new PowerShell window
Write-Host ""
Write-Host "?? Starting Angular Frontend with Proxy..." -ForegroundColor Green
Start-Process pwsh -ArgumentList @(
    "-NoExit",
    "-Command",
    "cd '$appPath'; Write-Host '?? Angular Frontend with Proxy' -ForegroundColor Cyan; ng serve"
) -WindowStyle Normal

Write-Host "   ? Frontend will be available at: http://localhost:4200" -ForegroundColor Gray

# Summary
Write-Host ""
Write-Host "? Development environment is starting!" -ForegroundColor Green
Write-Host ""
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  ?? API (Direct):         https://localhost:5001" -ForegroundColor White
Write-Host "  ?? Frontend:             http://localhost:4200" -ForegroundColor White
Write-Host "  ?? Proxied API:          http://localhost:4200/api/*" -ForegroundColor White
Write-Host "  ?? Swagger UI:           https://localhost:5001/swagger" -ForegroundColor White
Write-Host "  ??  Health Check (API):   https://localhost:5001/health" -ForegroundColor White
Write-Host "  ??  Health Check (Proxy): http://localhost:4200/health" -ForegroundColor White
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""
Write-Host "?? Tips:" -ForegroundColor Yellow
Write-Host "  • Access your app at: http://localhost:4200" -ForegroundColor Gray
Write-Host "  • API requests from Angular will be proxied automatically" -ForegroundColor Gray
Write-Host "  • No CORS issues! All requests appear same-origin" -ForegroundColor Gray
Write-Host "  • Press Ctrl+C in each window to stop the servers" -ForegroundColor Gray
Write-Host ""
Write-Host "  • If ports are in use, run: .\stop-dev.ps1" -ForegroundColor Gray
Write-Host ""

# Check if servers are up after a delay
Start-Sleep -Seconds 10

Write-Host "?? Checking if servers are running..." -ForegroundColor Yellow
Write-Host ""

# Check API
try {
    $apiResponse = Invoke-WebRequest -Uri "https://localhost:5001/health" -SkipCertificateCheck -ErrorAction SilentlyContinue
    if ($apiResponse.StatusCode -eq 200) {
        Write-Host "  ? API is UP and running!" -ForegroundColor Green
    }
} catch {
    Write-Host "  ??  API might still be starting... check the API terminal window" -ForegroundColor Yellow
}

# Check Angular (it takes longer to start)
try {
    $angularResponse = Invoke-WebRequest -Uri "http://localhost:4200" -ErrorAction SilentlyContinue
    if ($angularResponse.StatusCode -eq 200) {
        Write-Host "  ? Angular is UP and running!" -ForegroundColor Green
    }
} catch {
    Write-Host "  ??  Angular might still be compiling... check the Angular terminal window" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "?? Happy Coding!" -ForegroundColor Cyan
Write-Host ""
