# ?? LOGIN FAILING - Invalid Credentials

## Current Error

```
System.UnauthorizedAccessException: Invalid credentials.
  at LoginCommandHandler.Handle() line 82
```

This error occurs **BEFORE** the session/deviceId issues. The login is failing at the password verification step.

## What's Happening

The code at line 82 is:
```csharp
if (!credentialsValid)
{
    // ... audit log ...
    throw new UnauthorizedAccessException("Invalid credentials.");  // ? LINE 82
}
```

This means either:
1. ? Patient not found by email
2. ? Password doesn't match

## Debugging Steps

### Step 1: Check What You're Trying

What credentials are you using to login?
- Email: `patient@example.com`?
- Password: `Patient123!`?

### Step 2: Check If Patient Exists

Run this SQL query to see if the patient exists:

```sql
SELECT id, email, password_hash, email_verified
FROM patients
WHERE LOWER(email) = 'patient@example.com';
```

**Expected Result:**
- If NO rows: Patient doesn't exist (need to register first)
- If 1 row: Patient exists, password hash is there

### Step 3: Check If Email is Verified

The patient might exist but email might not be verified. Check the `email_verified` column.

### Step 4: Use Debugger Locals

Since you're in the debugger, check these local variables:

In the **Locals** window, look for:
- `patient` - Is it `null`? If yes, email not found
- `credentialsValid` - Is it `false`? If yes, password doesn't match
- `request.Email` - What email is being sent?
- `request.Password` - What password is being sent?

### Step 5: Add Breakpoint Earlier

Set a breakpoint at line 55 (before credential check):
```csharp
var patient = await _patientRepo.GetByEmailAsync(request.Email, cancellationToken);
```

Then step through:
1. Check if `patient` is null after this line
2. If not null, step into `Argon2.Verify` to see if it returns false

## Common Issues

### Issue 1: No Patient Registered
**Symptom**: `patient` is `null`

**Solution**: Register a test patient first:
```http
POST http://localhost:5000/api/auth/register
Content-Type: application/json

{
  "email": "patient@example.com",
  "password": "Patient123!",
  "name": "Test Patient",
  "phone": "1234567890",
  "dateOfBirth": "1990-01-01"
}
```

Then verify the email before trying to login.

### Issue 2: Email Not Verified
**Symptom**: Patient exists but `email_verified` is `false` or `null`

**Solution**: 
1. Check `email_verification_tokens` table for the token
2. Call the verify endpoint with that token
3. Or manually update: `UPDATE patients SET email_verified = true WHERE email = 'patient@example.com'`

### Issue 3: Wrong Password
**Symptom**: `patient` is not null but `credentialsValid` is `false`

**Solution**: 
- Make sure you're using the exact password you registered with
- Passwords are case-sensitive
- Check for extra spaces

### Issue 4: Password Hash Issue
**Symptom**: Argon2.Verify throws an exception

**Solution**: The password hash in the database might be corrupted. Re-register the patient.

## Quick Test Query

```sql
-- Check if patient exists and is verified
SELECT 
    id,
    email,
    email_verified,
    LENGTH(password_hash) as hash_length,
    created_at
FROM patients
WHERE LOWER(email) = 'patient@example.com';

-- Check verification token if email not verified
SELECT 
    token_hash,
    expires_at,
    used_at,
    created_at
FROM email_verification_tokens
WHERE patient_id = (SELECT id FROM patients WHERE LOWER(email) = 'patient@example.com');
```

## What To Do Right Now

1. **Check Debugger Locals**: Look at the `patient` variable
   - If `null` ? Email doesn't exist, need to register
   - If not `null` ? Check `credentialsValid` value

2. **Run SQL Query**: Check if patient exists in database

3. **Register If Needed**: Use the register endpoint to create a test patient

4. **Verify Email If Needed**: Complete email verification

## After Fixing Login

Once login succeeds, THEN we can test the deviceId/session flow that we fixed earlier.

The fixes for deviceId/session are correct, but they only matter AFTER login succeeds!

---

## Quick Commands

**Check patient in database:**
```powershell
# If using psql
psql -U postgres -d propeliq -c "SELECT id, email, email_verified FROM patients WHERE LOWER(email) = 'patient@example.com';"
```

**Register via API:**
```powershell
$body = @{
    email = "patient@example.com"
    password = "Patient123!"
    name = "Test Patient"
    phone = "1234567890"
    dateOfBirth = "1990-01-01"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/api/auth/register" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"
```

---

## Bottom Line

The "session_expired" error you were seeing was actually a **symptom** of trying to test the dashboard without being logged in successfully first.

**First fix the login** (register + verify patient), **then** test the complete flow including deviceId/session! ??
