# Login 401 Fix - Dashboard API Authentication Issue

## Issue Summary

When logging in from Angular, the login API call succeeds but the subsequent dashboard API call returns **401 Unauthorized**.

## Root Cause

The backend login/refresh endpoints were not returning `userId` and `role` fields in their responses, but the Angular `TokenResponse` interface expected these fields. This caused the Angular `AuthService` to store incomplete authentication state, preventing subsequent authenticated requests from working properly.

### Backend Response (Before Fix)
```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "expiresIn": 900
}
```

### Frontend Expected Response
```typescript
interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  userId: string;      // MISSING
  role: string;        // MISSING
}
```

## Files Modified

### 1. **Propel.Modules.Auth\Commands\LoginCommand.cs**
- Added `UserId` and `Role` properties to `LoginResult` record

### 2. **Propel.Modules.Auth\Handlers\LoginCommandHandler.cs**
- Updated `LoginResult` instantiation to include `patient.Id.ToString()` and `PatientRole`

### 3. **Propel.Api.Gateway\Controllers\AuthController.cs**
- Updated login endpoint response to include `result.UserId` and `result.Role`
- Updated refresh endpoint response to include `result.UserId` and `result.Role`

### 4. **Propel.Modules.Auth\Commands\RefreshTokenCommand.cs**
- Added `UserId` and `Role` properties to `RefreshTokenResult` record

### 5. **Propel.Modules.Auth\Handlers\RefreshTokenCommandHandler.cs**
- Updated `RefreshTokenResult` instantiation to include `userId.ToString()` and `role`

## Backend Response (After Fix)
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "a1b2c3d4...",
  "expiresIn": 900,
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "role": "Patient"
}
```

## Testing Steps

1. **Stop the debugger** (the application is currently being debugged, which prevents rebuild)
2. **Rebuild the solution**
   ```powershell
   dotnet build
   ```
3. **Restart the application**
4. **Test the login flow**:
   - Open the Angular app at `http://localhost:4200`
   - Navigate to login page
   - Enter valid credentials
   - Verify successful login
   - **Expected**: Dashboard loads successfully with patient data
   - **Verify**: No 401 error on dashboard API call

## Additional Notes

### AuthService Token Storage
The Angular `AuthService._storeTokens()` method now correctly populates all fields:
```typescript
private _storeTokens(res: TokenResponse): void {
  this._authState.set({
    accessToken: res.accessToken,
    refreshToken: res.refreshToken,
    userId: res.userId,          // ? Now populated
    role: res.role,              // ? Now populated
    expiresAt: Date.now() + res.expiresIn * 1_000,
  });
}
```

### JWT Claims
The JWT access token already contains these claims in its payload. This fix ensures the response body also includes them explicitly so the Angular client can store them in memory without parsing the JWT.

## Security Considerations

- ? No sensitive data exposure: `userId` is already in the JWT claims
- ? Role claim matches JWT role claim (validated on backend)
- ? Tokens remain in-memory only (no localStorage/sessionStorage)
- ? OWASP A02 compliance maintained

## Related Files
- Angular: `app/core/auth/auth-state.model.ts` - Defines TokenResponse interface
- Angular: `app/features/auth/services/auth.service.ts` - Consumes TokenResponse
- Angular: `app/core/interceptors/auth.interceptor.ts` - Uses stored tokens for Authorization header
- Backend: `Propel.Modules.Auth/Services/JwtService.cs` - Generates JWT with userId/role claims
