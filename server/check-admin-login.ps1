#!/usr/bin/env pwsh
# Script to check admin user in database and test login

Write-Host "=== Checking Admin User in Database ===" -ForegroundColor Cyan

# Check if Docker is running
$dockerRunning = docker ps 2>&1 | Select-String "postgres"
if (-not $dockerRunning) {
    Write-Host "? Docker is not running or PostgreSQL container is not started" -ForegroundColor Red
    Write-Host "Please start Docker and run: docker-compose up -d" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n1. Checking if admin user exists..." -ForegroundColor Green
docker exec -i propel-postgres psql -U propel_user -d propel_db -c "SELECT id, email, role, status, CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password, name FROM users WHERE email = 'admin@example.com';"

Write-Host "`n2. Checking all users in the database..." -ForegroundColor Green
docker exec -i propel-postgres psql -U propel_user -d propel_db -c "SELECT id, email, role, status, CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password, name FROM users ORDER BY created_at DESC LIMIT 5;"

Write-Host "`n3. Checking all patients in the database..." -ForegroundColor Green
docker exec -i propel-postgres psql -U propel_user -d propel_db -c "SELECT id, email, name, email_verified, CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password FROM patients ORDER BY created_at DESC LIMIT 5;"

Write-Host "`n=== Testing Login API ===" -ForegroundColor Cyan

# Check if backend is running
$backendUrl = "https://localhost:7295"
try {
    $pingResponse = Invoke-WebRequest -Uri "$backendUrl/api/auth/ping" -Method GET -SkipCertificateCheck -TimeoutSec 5 2>$null
    Write-Host "? Backend is running" -ForegroundColor Green
} catch {
    Write-Host "? Backend is not running at $backendUrl" -ForegroundColor Red
    Write-Host "Please start the backend server first" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n4. Testing admin login..." -ForegroundColor Green
$loginBody = @{
    email = "admin@example.com"
    password = "Admin@123"
    deviceId = "test-device-$(Get-Random)"
} | ConvertTo-Json

Write-Host "Request Body: $loginBody" -ForegroundColor Gray

try {
    $response = Invoke-RestMethod -Uri "$backendUrl/api/auth/login" `
        -Method POST `
        -Body $loginBody `
        -ContentType "application/json" `
        -SkipCertificateCheck

    Write-Host "? Login successful!" -ForegroundColor Green
    Write-Host "Access Token: $($response.accessToken.Substring(0, 50))..." -ForegroundColor Gray
    Write-Host "Role: $($response.role)" -ForegroundColor Cyan
    Write-Host "User ID: $($response.userId)" -ForegroundColor Cyan
} catch {
    Write-Host "? Login failed!" -ForegroundColor Red
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Yellow
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
    
    if ($_.ErrorDetails.Message) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Script Complete ===" -ForegroundColor Cyan
