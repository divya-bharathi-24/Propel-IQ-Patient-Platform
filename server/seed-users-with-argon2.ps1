#!/usr/bin/env pwsh
# Script to properly seed users with correct Argon2 hashes using project dependencies

Write-Host "=== Seeding Admin and Staff with Correct Argon2 Hashes ===" -ForegroundColor Cyan

$projectPath = "Propel.Api.Gateway/Propel.Api.Gateway.csproj"

Write-Host "`nGenerating Argon2id hashes using project dependencies..." -ForegroundColor Green

# Create a temporary console app that references the same Argon2 package
$tempScript = @'
using System;
using Isopoh.Cryptography.Argon2;

var adminPassword = "Admin@123";
var staffPassword = "Staff@123";

Console.WriteLine("ADMIN_HASH:" + Argon2.Hash(adminPassword));
Console.WriteLine("STAFF_HASH:" + Argon2.Hash(staffPassword));
'@

$tempDir = Join-Path $env:TEMP "PropelHashGenerator_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Isopoh.Cryptography.Argon2" Version="2.0.1" />
  </ItemGroup>
</Project>
'@

$programCs = Join-Path $tempDir "Program.cs"
$csprojFile = Join-Path $tempDir "HashGen.csproj"

$tempScript | Out-File -FilePath $programCs -Encoding UTF8
$csproj | Out-File -FilePath $csprojFile -Encoding UTF8

Push-Location $tempDir
try {
    Write-Host "Building hash generator..." -ForegroundColor Gray
    dotnet build -c Release --nologo -v q | Out-Null
    
    Write-Host "Generating hashes..." -ForegroundColor Gray
    $output = dotnet run --no-build -c Release
    
    $adminHash = ($output | Select-String "ADMIN_HASH:(.+)").Matches.Groups[1].Value
    $staffHash = ($output | Select-String "STAFF_HASH:(.+)").Matches.Groups[1].Value
    
    Write-Host "? Hashes generated successfully" -ForegroundColor Green
    
    # Create SQL
    $seedSql = @"
-- Ensure email uniqueness
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_email_lower ON users(LOWER(email));

-- Delete existing test users
DELETE FROM users WHERE email IN ('admin@example.com', 'staff@example.com');

-- Seed Admin User
INSERT INTO users (id, email, password_hash, role, status, credential_email_status, name, created_at)
VALUES (
    gen_random_uuid(),
    'admin@example.com',
    '$adminHash',
    1,
    0,
    'Sent',
    'System Administrator',
    NOW()
);

-- Seed Staff User  
INSERT INTO users (id, email, password_hash, role, status, credential_email_status, name, created_at)
VALUES (
    gen_random_uuid(),
    'staff@example.com',
    '$staffHash',
    0,
    0,
    'Sent',
    'Staff Member',
    NOW()
);

-- Verify seeded users
SELECT 
    id,
    email, 
    CASE role WHEN 0 THEN 'Staff' WHEN 1 THEN 'Admin' ELSE 'Unknown' END as role_name,
    CASE status WHEN 0 THEN 'Active' WHEN 1 THEN 'Deactivated' ELSE 'Unknown' END as status_name,
    name,
    CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password,
    credential_email_status
FROM users
WHERE email IN ('admin@example.com', 'staff@example.com')
ORDER BY role DESC;
"@

    Pop-Location
    
    Write-Host "`nExecuting SQL to seed database..." -ForegroundColor Green
    
    # Save to temp file and execute
    $tempSqlFile = [System.IO.Path]::GetTempFileName()
    $seedSql | Out-File -FilePath $tempSqlFile -Encoding UTF8
    
    $result = Get-Content $tempSqlFile | docker exec -i propel-postgres psql -U propel_user -d propel_db 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n? Users seeded successfully!" -ForegroundColor Green
        Write-Host "`nDatabase Results:" -ForegroundColor Cyan
        Write-Host $result
        
        Write-Host "`n? Test Credentials:" -ForegroundColor Green
        Write-Host "  Admin Login:" -ForegroundColor Cyan
        Write-Host "    Email: admin@example.com" -ForegroundColor White
        Write-Host "    Password: Admin@123" -ForegroundColor White
        Write-Host ""
        Write-Host "  Staff Login:" -ForegroundColor Cyan
        Write-Host "    Email: staff@example.com" -ForegroundColor White
        Write-Host "    Password: Staff@123" -ForegroundColor White
        
        Write-Host "`nNext steps:" -ForegroundColor Yellow
        Write-Host "  1. Make sure backend is running: dotnet run --project Propel.Api.Gateway" -ForegroundColor White
        Write-Host "  2. Test login: ./test-admin-login-complete.ps1" -ForegroundColor White
        Write-Host "  3. Or use Swagger UI: https://localhost:7295/swagger" -ForegroundColor White
    } else {
        Write-Host "`n? Failed to seed database" -ForegroundColor Red
        Write-Host $result
    }
    
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
    
} catch {
    Write-Host "`n? Error: $($_.Exception.Message)" -ForegroundColor Red
    Pop-Location
} finally {
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== Script Complete ===" -ForegroundColor Cyan
