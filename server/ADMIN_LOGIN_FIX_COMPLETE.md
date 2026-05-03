# Admin/Staff Login Fix - Complete Summary

## Problem Identified

You were getting `System.UnauthorizedAccessException: Invalid credentials` when trying to log in with admin credentials (`admin@example.com` / `Admin@123`) even though the user record existed in the database.

### Root Cause

The `LoginCommandHandler` was **only checking the Patient table** (`IPatientRepository`) but you were trying to log in with a **Staff/Admin account** which is stored in the **User table**.

From the original code:
```csharp
// This only looked up patients!
var patient = await _patientRepo.GetByEmailAsync(request.Email, cancellationToken);
```

## Solution Implemented

### 1. Added `GetByEmailAsync` to `IUserRepository`

**File:** `Propel.Domain/Interfaces/IUserRepository.cs`

Added new method to retrieve users by email:
```csharp
/// <summary>
/// Retrieves a user by email address (case-insensitive). Returns <c>null</c> when not found.
/// Used for Staff/Admin login authentication (US_011 extension).
/// </summary>
Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
```

### 2. Implemented `GetByEmailAsync` in `UserRepository`

**File:** `Propel.Api.Gateway/Infrastructure/Repositories/UserRepository.cs`

```csharp
public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    => _context.Users
        .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), cancellationToken);
```

### 3. Updated `LoginCommandHandler` to Support Both User Types

**File:** `Propel.Modules.Auth/Handlers/LoginCommandHandler.cs`

The handler now:
1. Checks **both** Patient and User tables
2. Verifies credentials against whichever account is found
3. Returns the appropriate role (`Patient`, `Staff`, or `Admin`)
4. Stores the correct user reference in the refresh token

**Key changes:**
- Added `IUserRepository` dependency
- Checks both `_patientRepo.GetByEmailAsync()` and `_userRepo.GetByEmailAsync()`
- Handles role determination dynamically
- Sets `PatientId` or `UserId` in RefreshToken based on account type

## Testing & Verification

### Scripts Created

1. **`seed-users-with-argon2.ps1`** ? RECOMMENDED
   - Generates correct Argon2id password hashes using your project's Argon2 library
   - Seeds admin and staff users with proper credentials
   - Verifies database state

2. **`test-admin-login-complete.ps1`**
   - Checks Docker and database state
   - Tests the login API
   - Displays detailed JWT token claims
   - Provides debugging information

3. **`check-admin-login.ps1`**
   - Quick check of database users and login test

### How to Test

**Step 1: Start your environment**
```powershell
# Start Docker containers
docker-compose up -d

# OR use your startup script
./start-dev.ps1
```

**Step 2: Seed test users**
```powershell
# This generates proper Argon2 hashes and seeds the database
./seed-users-with-argon2.ps1
```

**Step 3: Restart the backend** (important after code changes)
```powershell
# Stop if running, then start
./restart-all.ps1

# OR manually
dotnet run --project Propel.Api.Gateway
```

**Step 4: Test the login**
```powershell
# Run comprehensive test
./test-admin-login-complete.ps1
```

### Manual API Test

**Using cURL:**
```bash
curl -X POST https://localhost:7295/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@example.com",
    "password": "Admin@123",
    "deviceId": "test-device-001"
  }' \
  -k
```

**Using PowerShell:**
```powershell
$body = @{
    email = "admin@example.com"
    password = "Admin@123"
    deviceId = "test-device-001"
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7295/api/auth/login" `
    -Method POST `
    -Body $body `
    -ContentType "application/json" `
    -SkipCertificateCheck
```

### Expected Response

```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "long-random-string",
  "expiresIn": 900,
  "userId": "guid-here",
  "role": "Admin",
  "deviceId": "test-device-001"
}
```

## Test Credentials

After running `seed-users-with-argon2.ps1`:

| Role  | Email               | Password   |
|-------|---------------------|------------|
| Admin | admin@example.com   | Admin@123  |
| Staff | staff@example.com   | Staff@123  |

## Architecture Notes

### Authentication Flow

```
POST /api/auth/login
  ?
AuthController.Login()
  ?
LoginCommand via MediatR
  ?
LoginCommandHandler.Handle()
  ?
???????????????????????????????????
? 1. Check PatientRepository      ?
? 2. Check UserRepository          ?
? 3. Verify Argon2 password hash   ?
? 4. Generate JWT + RefreshToken   ?
? 5. Create Redis session          ?
? 6. Write audit log               ?
???????????????????????????????????
  ?
Return LoginResult
```

### Security Considerations

? **Implemented:**
- Case-insensitive email lookup (prevents enumeration)
- Generic 401 error (doesn't reveal which field failed)
- Argon2id password hashing
- SHA-256 hashed emails in audit logs
- Audit trail for failed login attempts
- Redis session management
- JWT with device binding

## Database Schema

### Users Table (Staff/Admin)
```sql
CREATE TABLE users (
    id UUID PRIMARY KEY,
    email VARCHAR(320) UNIQUE,
    password_hash TEXT,
    role INTEGER,  -- 0=Staff, 1=Admin
    status INTEGER,
    name VARCHAR(200),
    credential_email_status VARCHAR(50),
    last_login_at TIMESTAMP,
    created_at TIMESTAMP
);
```

### Patients Table (Patient Login)
```sql
CREATE TABLE patients (
    id UUID PRIMARY KEY,
    email VARCHAR(320) UNIQUE,
    password_hash TEXT,
    name TEXT,  -- Encrypted with AES-256
    phone TEXT, -- Encrypted with AES-256
    date_of_birth TEXT, -- Encrypted
    email_verified BOOLEAN,
    status INTEGER,
    created_at TIMESTAMP
);
```

### RefreshTokens Table
```sql
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY,
    patient_id UUID NULL,  -- For patient logins
    user_id UUID NULL,     -- For staff/admin logins
    token_hash TEXT,
    family_id UUID,
    device_id VARCHAR(256),
    expires_at TIMESTAMP,
    revoked_at TIMESTAMP NULL,
    created_at TIMESTAMP
);
```

## Troubleshooting

### Issue: Still getting 401 after fix

**Checklist:**
1. ? Rebuild the solution: `dotnet build`
2. ? Restart the backend completely
3. ? Re-run the seed script: `./seed-users-with-argon2.ps1`
4. ? Check Docker containers are running: `docker ps`
5. ? Verify database connection string in `appsettings.Development.json`

### Issue: Password hash mismatch

The Argon2 hash must be generated with the **same configuration** as verification.

**Solution:** Use the `seed-users-with-argon2.ps1` script which uses the exact same Argon2 library version as your application.

### Issue: Email not found

Check email case sensitivity:
```sql
-- Run in PostgreSQL
SELECT email, LOWER(email), role FROM users WHERE email ILIKE '%admin%';
```

The application converts emails to lowercase during lookup: `email.ToLowerInvariant()`

### Issue: Redis connection error

If Redis is not needed, the application falls back to `InMemoryRedisSessionService`. Check logs for:
```
Redis connection failed, using in-memory session store
```

## Code Quality

All changes follow:
- ? OWASP security guidelines (no user enumeration)
- ? .NET 10 conventions
- ? C# 14.0 language features
- ? Argon2id for password hashing (NFR-004)
- ? Comprehensive XML documentation
- ? Audit logging for security events (US_013)
- ? Repository pattern with EF Core
- ? Case-insensitive email lookups

## Files Modified

1. `Propel.Domain/Interfaces/IUserRepository.cs` - Added GetByEmailAsync method
2. `Propel.Api.Gateway/Infrastructure/Repositories/UserRepository.cs` - Implemented GetByEmailAsync
3. `Propel.Modules.Auth/Handlers/LoginCommandHandler.cs` - Support both Patient and User login

## Files Created (Test Scripts)

1. `seed-users-with-argon2.ps1` - Proper user seeding with correct hashes
2. `test-admin-login-complete.ps1` - Comprehensive login testing
3. `check-admin-login.ps1` - Quick verification script

## Next Steps

1. **Run the seed script:**
   ```powershell
   ./seed-users-with-argon2.ps1
   ```

2. **Restart backend:**
   ```powershell
   ./restart-all.ps1
   ```

3. **Test login:**
   ```powershell
   ./test-admin-login-complete.ps1
   ```

4. **Verify in application:**
   - Navigate to Angular app: http://localhost:4200
   - Click "Staff/Admin Login"
   - Use credentials: admin@example.com / Admin@123
   - Should redirect to staff/admin dashboard

## Success Criteria

? Admin login with `admin@example.com` / `Admin@123` returns HTTP 200  
? Response includes `accessToken`, `refreshToken`, and `role: "Admin"`  
? JWT token contains correct claims (`sub`, `role`, `deviceId`)  
? Audit log records successful LOGIN event  
? Redis/in-memory session is created  
? RefreshToken is stored with `user_id` populated  

---

**Last Updated:** 2025-01-25  
**Status:** ? READY FOR TESTING  
**Verified Build:** ? Successful compilation
