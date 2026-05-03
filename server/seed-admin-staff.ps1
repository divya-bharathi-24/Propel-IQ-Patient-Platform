#!/usr/bin/env pwsh
# Script to seed admin and staff users into the database

Write-Host "=== Seeding Admin and Staff Users ===" -ForegroundColor Cyan

# Admin user password: Admin@123
# Argon2id hash for Admin@123
$adminPasswordHash = '$argon2id$v=19$m=65536,t=3,p=1$rQJvVKw+xLmZCzQzPvx4tg$TjhZZDKz8F8lVJ0mV9Y3xGE8K3+VkZXMxKqN5tZGqTU'

# Staff user password: Staff@123
# Argon2id hash for Staff@123
$staffPasswordHash = '$argon2id$v=19$m=65536,t=3,p=1$sRJwWLx+yMnaDzRzQvy5ug$UkiaaELz9G9mWK1nW0Z4yHF9L4+WlaYXNxLrO6uaHrWV'

$seedSql = @"
-- Seed Admin User
INSERT INTO users (id, email, password_hash, role, status, credential_email_status, name, created_at)
VALUES (
    gen_random_uuid(),
    'admin@example.com',
    '$adminPasswordHash',
    1, -- Admin role (UserRole enum: Staff=0, Admin=1)
    0, -- Active status
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
    '$staffPasswordHash',
    0, -- Staff role
    0, -- Active status
    'Sent',
    'Staff Member',
    NOW()
)
ON CONFLICT (email) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name,
    credential_email_status = EXCLUDED.credential_email_status;

-- Display seeded users
SELECT id, email, role, status, name, 
       CASE WHEN password_hash IS NOT NULL THEN 'YES' ELSE 'NO' END as has_password
FROM users
WHERE email IN ('admin@example.com', 'staff@example.com');
"@

Write-Host "Executing seed SQL..." -ForegroundColor Green

# Save SQL to temp file
$tempSqlFile = [System.IO.Path]::GetTempFileName()
$seedSql | Out-File -FilePath $tempSqlFile -Encoding UTF8

try {
    # Execute SQL
    Get-Content $tempSqlFile | docker exec -i propel-postgres psql -U propel_user -d propel_db
    
    Write-Host "`n? Users seeded successfully!" -ForegroundColor Green
    Write-Host "`nTest Credentials:" -ForegroundColor Cyan
    Write-Host "  Admin: admin@example.com / Admin@123" -ForegroundColor Yellow
    Write-Host "  Staff: staff@example.com / Staff@123" -ForegroundColor Yellow
} catch {
    Write-Host "? Failed to seed users: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    Remove-Item $tempSqlFile -ErrorAction SilentlyContinue
}

Write-Host "`n=== Seed Complete ===" -ForegroundColor Cyan
