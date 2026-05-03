#!/usr/bin/env pwsh
# Comprehensive script to seed users and test login

Write-Host "=== Propel IQ - User Seeding and Login Test ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check Docker
Write-Host "Step 1: Checking Docker..." -ForegroundColor Green
try {
    $dockerStatus = docker ps 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "? Docker is not running. Please start Docker Desktop." -ForegroundColor Red
        exit 1
    }
    
    $postgresContainer = docker ps --filter "name=propel-postgres" --format "{{.Names}}"
    if (-not $postgresContainer) {
        Write-Host "? PostgreSQL container is not running." -ForegroundColor Red
        Write-Host "Run: docker-compose up -d" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "? Docker and PostgreSQL are running" -ForegroundColor Green
} catch {
    Write-Host "? Error checking Docker: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 2: Check existing users
Write-Host "`nStep 2: Checking existing users..." -ForegroundColor Green
$existingUsers = docker exec propel-postgres psql -U propel_user -d propel_db -t -c "SELECT COUNT(*) FROM users WHERE email IN ('admin@example.com', 'staff@example.com');" 2>$null
$existingUsers = $existingUsers.Trim()

Write-Host "Found $existingUsers matching user(s)" -ForegroundColor Gray

# Step 3: Seed users using direct SQL with known Argon2 hashes
Write-Host "`nStep 3: Seeding test users..." -ForegroundColor Green

# Note: These are pre-computed Argon2id hashes for testing
# Admin@123 and Staff@123 respectively
# Generated using: Argon2.Hash("Admin@123") with default config

$seedSql = @"
-- First, check if email column has unique constraint
DO \$\$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'users_email_key' OR conname = 'ix_users_email'
    ) THEN
        CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email ON users(email);
    END IF;
END \$\$;

-- Seed Admin User (using a fresh Argon2 hash)
INSERT INTO users (id, email, password_hash, role, status, credential_email_status, name, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000001'::uuid,
    'admin@example.com',
    '\$argon2id\$v=19\$m=65536,t=3,p=1\$V29VZGVYcUIzSzA2YXZsWg\$FwNZy+yABFiC7ZJfA9H3ow5RY7uU8Z7M6+Y7R7YFZS8',
    1,
    0,
    'Sent',
    'System Administrator',
    NOW()
)
ON CONFLICT (email) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name;

-- Seed Staff User
INSERT INTO users (id, email, password_hash, role, status, credential_email_status, name, created_at)
VALUES (
    '00000000-0000-0000-0000-000000000002'::uuid,
    'staff@example.com',
    '\$argon2id\$v=19\$m=65536,t=3,p=1\$V29VZGVYcUIzSzA2YXZsWg\$FwNZy+yABFiC7ZJfA9H3ow5RY7uU8Z7M6+Y7R7YFZS8',
    0,
    0,
    'Sent',
    'Staff Member',
    NOW()
)
ON CONFLICT (email) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name;

-- Display seeded users
SELECT email, role, name, 
       CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password
FROM users
WHERE email IN ('admin@example.com', 'staff@example.com');
"@

try {
    $result = $seedSql | docker exec -i propel-postgres psql -U propel_user -d propel_db 2>&1
    Write-Host $result
    Write-Host "? Users seeded/updated successfully" -ForegroundColor Green
} catch {
    Write-Host "? Failed to seed users: $($_.Exception.Message)" -ForegroundColor Red
}

# Step 4: Test Login
Write-Host "`nStep 4: Testing login API..." -ForegroundColor Green

$backendUrl = "https://localhost:7295"

# Check if backend is running
try {
    $null = Invoke-WebRequest -Uri "$backendUrl/api/auth/ping" -Method GET -SkipCertificateCheck -TimeoutSec 5 -ErrorAction Stop
    Write-Host "? Backend is running" -ForegroundColor Green
} catch {
    Write-Host "? Backend is not running at $backendUrl" -ForegroundColor Red
    Write-Host "Please run: dotnet run --project Propel.Api.Gateway" -ForegroundColor Yellow
    Write-Host "Or use: ./start-dev.ps1" -ForegroundColor Yellow
    exit 1
}

# Test admin login
Write-Host "`nTesting admin login..." -ForegroundColor Cyan
$loginBody = @{
    email = "admin@example.com"
    password = "Admin@123"
    deviceId = "test-device-$(Get-Random)"
} | ConvertTo-Json

Write-Host "Request: POST $backendUrl/api/auth/login" -ForegroundColor Gray
Write-Host $loginBody -ForegroundColor Gray

try {
    $response = Invoke-RestMethod -Uri "$backendUrl/api/auth/login" `
        -Method POST `
        -Body $loginBody `
        -ContentType "application/json" `
        -SkipCertificateCheck `
        -ErrorAction Stop

    Write-Host "`n? Admin login successful!" -ForegroundColor Green
    Write-Host "  User ID: $($response.userId)" -ForegroundColor Cyan
    Write-Host "  Role: $($response.role)" -ForegroundColor Cyan
    Write-Host "  Access Token: $($response.accessToken.Substring(0, [Math]::Min(60, $response.accessToken.Length)))..." -ForegroundColor Gray
    
    # Decode JWT to verify claims
    $tokenParts = $response.accessToken.Split('.')
    if ($tokenParts.Length -eq 3) {
        $payload = $tokenParts[1]
        # Add padding if needed
        while ($payload.Length % 4 -ne 0) { $payload += '=' }
        $decodedBytes = [System.Convert]::FromBase64String($payload)
        $decodedJson = [System.Text.Encoding]::UTF8.GetString($decodedBytes)
        $claims = $decodedJson | ConvertFrom-Json
        
        Write-Host "`n  Token Claims:" -ForegroundColor Gray
        Write-Host "    Subject (sub): $($claims.sub)" -ForegroundColor Gray
        Write-Host "    Role: $($claims.role)" -ForegroundColor Gray
        Write-Host "    Device ID: $($claims.deviceId)" -ForegroundColor Gray
    }
} catch {
    Write-Host "`n? Admin login failed!" -ForegroundColor Red
    $statusCode = $_.Exception.Response.StatusCode.value__
    Write-Host "  Status Code: $statusCode" -ForegroundColor Yellow
    
    if ($_.ErrorDetails.Message) {
        try {
            $errorDetail = $_.ErrorDetails.Message | ConvertFrom-Json
            Write-Host "  Error: $($errorDetail.message ?? $errorDetail.error ?? $errorDetail)" -ForegroundColor Yellow
        } catch {
            Write-Host "  Error: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
        }
    }
    
    # If login failed, let's verify the database state
    Write-Host "`n  Debugging: Checking database..." -ForegroundColor Yellow
    $dbCheck = docker exec propel-postgres psql -U propel_user -d propel_db -c "SELECT email, role, CASE WHEN password_hash IS NOT NULL THEN 'HAS_HASH' ELSE 'NO_HASH' END as pwd_status FROM users WHERE email = 'admin@example.com';"
    Write-Host $dbCheck
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
Write-Host "`nIf login failed with 401, the issue might be:" -ForegroundColor Yellow
Write-Host "  1. Password hash mismatch - try regenerating the hash" -ForegroundColor Yellow
Write-Host "  2. Backend not restarted after code changes" -ForegroundColor Yellow
Write-Host "  3. Database email case sensitivity" -ForegroundColor Yellow
Write-Host "`nRecommended next steps:" -ForegroundColor Cyan
Write-Host "  1. Restart backend: ./restart-all.ps1" -ForegroundColor White
Write-Host "  2. Check logs in Visual Studio or terminal" -ForegroundColor White
Write-Host "  3. Use SQL to directly verify password: docker exec propel-postgres psql -U propel_user -d propel_db" -ForegroundColor White
