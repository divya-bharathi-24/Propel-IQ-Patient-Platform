# Token Authentication Debug Guide

## Current Issue
Dashboard API call returns **401 Unauthorized** after successful login, indicating the JWT token is not being attached to the request.

## What I've Done

### 1. Backend Changes ?
- Added `userId` and `role` to login/refresh responses
- Files modified:
  - `LoginCommand.cs` and `LoginCommandHandler.cs`
  - `RefreshTokenCommand.cs` and `RefreshTokenCommandHandler.cs`
  - `AuthController.cs`

### 2. Frontend Debug Logging ?
- Added console logging to `AuthService._storeTokens()`
- This will show:
  - Whether tokens are received from backend
  - Whether they're being stored correctly
  - Authentication state after storage

## How to Debug

### Step 1: Restart Backend
**IMPORTANT**: The backend is currently being debugged. You need to:
1. **Stop the debugger** in Visual Studio
2. **Rebuild the solution**: `dotnet build`
3. **Restart the application**

### Step 2: Restart Angular Dev Server
The Angular app needs to pick up the code changes:
```powershell
# Stop current dev server (Ctrl+C)
# Then restart
cd ..\app
npm start
```

### Step 3: Test Login Flow with Browser Console Open
1. Open **Chrome DevTools** (F12)
2. Go to **Console** tab
3. Clear console
4. Navigate to `http://localhost:4200/auth/login`
5. Enter credentials and login
6. Watch the console output

### Expected Console Output

#### On Login Success:
```
[AuthService] Storing tokens: {
  hasAccessToken: true,
  hasRefreshToken: true,
  userId: "550e8400-e29b-...",
  role: "Patient",
  expiresIn: 900
}

[AuthService] Token state after storage: {
  hasAccessToken: true,
  isAuthenticated: true,
  expiresAt: "2024-04-22T12:15:00.000Z"
}
```

#### On Dashboard API Call:
```
[AuthInterceptor] Request URL: /api/patient/dashboard
[AuthInterceptor] Access token: eyJhbGciOiJIUzI1NiIs...
[AuthInterceptor] isAuthenticated: true
```

### Step 4: Check Network Tab
1. Open **Network** tab in DevTools
2. Find the `/api/patient/dashboard` request
3. Click on it
4. Go to **Headers** section
5. Look for **Request Headers**
6. Verify `Authorization: Bearer <token>` is present

## Possible Issues & Solutions

### Issue 1: No `userId`/`role` in Backend Response
**Symptom**: Console shows:
```
[AuthService] Storing tokens: {
  userId: undefined,
  role: undefined
}
```

**Solution**: Backend not rebuilt. Go back to Step 1.

### Issue 2: Token is NULL in Interceptor
**Symptom**: Console shows:
```
[AuthInterceptor] Access token: NULL
[AuthInterceptor] isAuthenticated: false
```

**Causes**:
1. **Login didn't store token** - Check Step 3 output
2. **Token expired before dashboard call** - Check `expiresAt` timestamp
3. **Race condition** - Token stored but signal not updated yet

**Solution**:
```typescript
// In login.component.ts, change from:
this.router.navigate(['/dashboard']);

// To:
setTimeout(() => {
  this.router.navigate(['/dashboard']);
}, 100);
```

### Issue 3: 401 After Token Refresh Attempt
**Symptom**: Console shows:
```
[AuthInterceptor] 401 error, attempting refresh...
```

**Cause**: Backend refresh endpoint not returning `userId`/`role`.

**Solution**: Verify `AuthController.Refresh()` includes these fields.

## Quick Test Commands

### Check Backend Response Format
```powershell
# Login and capture response
curl -X POST http://localhost:5000/api/auth/login `
  -H "Content-Type: application/json" `
  -d '{"email":"test@example.com","password":"Test1234!"}' | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

Expected output:
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "abc...",
  "expiresIn": 900,
  "userId": "550e8400-...",  // ? MUST BE PRESENT
  "role": "Patient"          // ? MUST BE PRESENT
}
```

### Check Request Headers
```powershell
# After login, test dashboard with captured token
$token = "YOUR_ACCESS_TOKEN_HERE"
curl -H "Authorization: Bearer $token" http://localhost:5000/api/patient/dashboard
```

Expected: **200 OK** with dashboard data

## Next Steps

1. ? Stop debugger
2. ? Rebuild backend: `dotnet build`
3. ? Restart backend application
4. ? Restart Angular dev server
5. ? Open browser console
6. ? Login and watch console output
7. ? Share console output in chat

## If Still Failing

Share this information:
1. **Console output** from login attempt
2. **Network tab** screenshot showing:
   - Login response body
   - Dashboard request headers
3. **Backend logs** (if any errors)

The debug logs will tell us exactly where the flow is breaking!
