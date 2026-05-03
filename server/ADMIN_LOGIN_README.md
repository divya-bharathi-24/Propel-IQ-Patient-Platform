# ?? Admin Login Issue - FIXED

## Problem
Getting `401 Unauthorized` when logging in with `admin@example.com` despite user existing in database.

## Root Cause
The `LoginCommandHandler` only checked the **Patient** table, but admin accounts are stored in the **User** table.

## Solution Applied ?

### Code Changes (3 files):

1. **`IUserRepository.cs`** - Added `GetByEmailAsync()` method
2. **`UserRepository.cs`** - Implemented email lookup  
3. **`LoginCommandHandler.cs`** - Now checks **both** Patient and User tables

### Build Status: ? SUCCESSFUL

---

## ?? Quick Start (3 Steps)

### Option A: Automated (Recommended)

```powershell
# Run this single command
./quick-fix-admin-login.ps1
```

### Option B: Manual Steps

```powershell
# 1. Seed test users with correct passwords
./seed-users-with-argon2.ps1

# 2. Restart the backend (CRITICAL!)
./restart-all.ps1
# OR stop and restart: dotnet run --project Propel.Api.Gateway

# 3. Test login
./test-admin-login-complete.ps1
```

---

## ?? Test Credentials

| Role  | Email             | Password  |
|-------|-------------------|-----------|
| Admin | admin@example.com | Admin@123 |
| Staff | staff@example.com | Staff@123 |

---

## ? Verification

**Test via cURL:**
```bash
curl -X POST https://localhost:7295/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"Admin@123","deviceId":"test"}' \
  -k
```

**Expected Response:**
```json
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "...",
  "expiresIn": 900,
  "userId": "guid",
  "role": "Admin",
  "deviceId": "test"
}
```

---

## ?? Troubleshooting

| Issue | Solution |
|-------|----------|
| Still 401 | **Restart backend** after code changes |
| Hash mismatch | Run `seed-users-with-argon2.ps1` |
| Docker error | Start Docker: `docker-compose up -d` |
| Backend not running | `dotnet run --project Propel.Api.Gateway` |

---

## ?? Documentation

- **Complete Guide:** `ADMIN_LOGIN_FIX_COMPLETE.md`
- **Architecture:** See "Authentication Flow" section in complete guide
- **Security:** OWASP-compliant (A01, A07, A09)

---

## ?? What Changed?

**Before:**
```csharp
// Only checked patients
var patient = await _patientRepo.GetByEmailAsync(email);
```

**After:**
```csharp
// Checks both patients AND staff/admin users
var patient = await _patientRepo.GetByEmailAsync(email);
var user = await _userRepo.GetByEmailAsync(email);
// ... verify credentials for whichever exists
```

---

## ?? Important Notes

1. **Must restart backend** after applying code changes
2. Use `seed-users-with-argon2.ps1` for proper password hashes
3. Both Patient and Staff/Admin can login via same endpoint
4. Refresh tokens now support both `patient_id` and `user_id`

---

**Status:** ? READY FOR TESTING  
**Build:** ? Successful  
**Tests:** ?? Run `quick-fix-admin-login.ps1`

---

Need help? Check `ADMIN_LOGIN_FIX_COMPLETE.md` for detailed information.
