#!/usr/bin/env pwsh
# QUICK FIX - Run this script to fix admin login issue

Write-Host "=== Quick Fix for Admin Login Issue ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

$dockerRunning = docker ps 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "? Docker not running. Start Docker Desktop and try again." -ForegroundColor Red
    exit 1
}

$postgresRunning = docker ps --filter "name=propel-postgres" --format "{{.Names}}"
if (-not $postgresRunning) {
    Write-Host "? PostgreSQL not running. Run: docker-compose up -d" -ForegroundColor Red
    exit 1
}

Write-Host "? Prerequisites OK" -ForegroundColor Green

# Step 2: Seed users
Write-Host "`nSeeding admin and staff users..." -ForegroundColor Yellow
& "$PSScriptRoot/seed-users-with-argon2.ps1"

if ($LASTEXITCODE -ne 0) {
    Write-Host "? Failed to seed users" -ForegroundColor Red
    exit 1
}

# Step 3: Check if backend is running
Write-Host "`nChecking backend..." -ForegroundColor Yellow
try {
    $null = Invoke-WebRequest -Uri "https://localhost:7295/api/auth/ping" -Method GET -SkipCertificateCheck -TimeoutSec 3 -ErrorAction Stop
    Write-Host "? Backend is running" -ForegroundColor Green
    
    Write-Host "`n??  IMPORTANT: Please restart the backend to apply code changes!" -ForegroundColor Yellow
    Write-Host "   Press Ctrl+C in the terminal running dotnet, then restart" -ForegroundColor White
    Write-Host "   Or run: ./restart-all.ps1" -ForegroundColor White
    
    $restart = Read-Host "`nHave you restarted the backend? (y/n)"
    if ($restart -ne 'y') {
        Write-Host "Please restart the backend and run this script again" -ForegroundColor Yellow
        exit 0
    }
} catch {
    Write-Host "? Backend not running. Start it with:" -ForegroundColor Red
    Write-Host "   dotnet run --project Propel.Api.Gateway" -ForegroundColor White
    Write-Host "   Or: ./start-dev.ps1" -ForegroundColor White
    exit 1
}

# Step 4: Test login
Write-Host "`nTesting admin login..." -ForegroundColor Yellow

$loginBody = @{
    email = "admin@example.com"
    password = "Admin@123"
    deviceId = "quickfix-$(Get-Random)"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "https://localhost:7295/api/auth/login" `
        -Method POST `
        -Body $loginBody `
        -ContentType "application/json" `
        -SkipCertificateCheck `
        -ErrorAction Stop

    Write-Host "`n? SUCCESS! Admin login is working!" -ForegroundColor Green
    Write-Host "   Role: $($response.role)" -ForegroundColor Cyan
    Write-Host "   User ID: $($response.userId)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "You can now login with:" -ForegroundColor White
    Write-Host "   Email: admin@example.com" -ForegroundColor Cyan
    Write-Host "   Password: Admin@123" -ForegroundColor Cyan
    
} catch {
    Write-Host "`n? Login failed!" -ForegroundColor Red
    Write-Host "   Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Yellow
    
    if ($_.ErrorDetails.Message) {
        Write-Host "   Error: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
    
    Write-Host "`nPlease check:" -ForegroundColor Yellow
    Write-Host "   1. Backend is running and restarted after code changes" -ForegroundColor White
    Write-Host "   2. Check Visual Studio Output window for errors" -ForegroundColor White
    Write-Host "   3. Review ADMIN_LOGIN_FIX_COMPLETE.md for details" -ForegroundColor White
}

Write-Host "`n=== Quick Fix Complete ===" -ForegroundColor Cyan
