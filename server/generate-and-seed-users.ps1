#!/usr/bin/env pwsh
# Script to generate Argon2id password hashes and seed users

Write-Host "=== Generating Password Hashes ===" -ForegroundColor Cyan

# Create a temporary C# file to generate Argon2 hashes
$csharpCode = @'
using System;
using Isopoh.Cryptography.Argon2;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: GenerateHash <password>");
            return;
        }

        string password = args[0];
        string hash = Argon2.Hash(password);
        Console.WriteLine(hash);
    }
}
'@

$tempDir = Join-Path $env:TEMP "PropelHashGen"
$null = New-Item -ItemType Directory -Path $tempDir -Force

$csprojContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Isopoh.Cryptography.Argon2" Version="2.0.1" />
  </ItemGroup>
</Project>
'@

$csprojPath = Join-Path $tempDir "HashGen.csproj"
$csPath = Join-Path $tempDir "Program.cs"

$csharpCode | Out-File -FilePath $csPath -Encoding UTF8
$csprojContent | Out-File -FilePath $csprojPath -Encoding UTF8

Write-Host "Building hash generator..." -ForegroundColor Gray

Push-Location $tempDir
try {
    dotnet build -c Release -o . 2>&1 | Out-Null
    
    Write-Host "Generating Argon2id hashes..." -ForegroundColor Green
    $adminHash = & dotnet "$tempDir/HashGen.dll" "Admin@123"
    $staffHash = & dotnet "$tempDir/HashGen.dll" "Staff@123"
    
    Write-Host "Admin password hash generated" -ForegroundColor Gray
    Write-Host "Staff password hash generated" -ForegroundColor Gray
    
    # Create SQL with generated hashes
    $seedSql = @"
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
)
ON CONFLICT (email) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name,
    credential_email_status = EXCLUDED.credential_email_status;

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
)
ON CONFLICT (email) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name,
    credential_email_status = EXCLUDED.credential_email_status;

SELECT id, email, role, status, name, 
       CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password
FROM users
WHERE email IN ('admin@example.com', 'staff@example.com');
"@

    Write-Host "`n=== Seeding Users to Database ===" -ForegroundColor Cyan
    
    # Save SQL to temp file
    $tempSqlFile = [System.IO.Path]::GetTempFileName()
    $seedSql | Out-File -FilePath $tempSqlFile -Encoding UTF8
    
    # Execute SQL
    Get-Content $tempSqlFile | docker exec -i propel-postgres psql -U propel_user -d propel_db
    
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
    
    Write-Host "`n? Users seeded successfully!" -ForegroundColor Green
    Write-Host "`nTest Credentials:" -ForegroundColor Cyan
    Write-Host "  Admin: admin@example.com / Admin@123" -ForegroundColor Yellow
    Write-Host "  Staff: staff@example.com / Staff@123" -ForegroundColor Yellow
    
} catch {
    Write-Host "? Error: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    Pop-Location
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== Script Complete ===" -ForegroundColor Cyan
