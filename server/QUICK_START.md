# ?? Quick Start - Propel IQ Development

## Start Backend (Terminal 1)
```powershell
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\server\Propel.Api.Gateway
dotnet run --launch-profile https
```
Wait for: `Now listening on: https://localhost:5001`

## Start Frontend with Proxy (Terminal 2)
```powershell
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\app
ng serve
```
Wait for: `[HPM] Proxy created: /api -> https://localhost:5001`

## Access Your App
- **Frontend**: http://localhost:4200
- **API (direct)**: https://localhost:5001/swagger
- **API (through proxy)**: http://localhost:4200/api/*

## How It Works
```
Browser ? http://localhost:4200/api/Auth/register
         ? (Angular Proxy forwards)
         https://localhost:5001/api/Auth/register
         ? (API responds)
Browser ? Response (No CORS issues!)
```

## Test Registration
### From Browser (Recommended)
1. Go to: http://localhost:4200/auth/register
2. Fill the form and submit
3. Request goes through proxy automatically

### From PowerShell
```powershell
curl.exe -X POST http://localhost:4200/api/Auth/register `
  -H "Content-Type: application/json" `
  -d '{\"name\":\"Jothish\",\"email\":\"test@example.com\",\"password\":\"Test@123\",\"phone\":\"1234567890\",\"dateOfBirth\":\"2000-01-01\"}'
```

## Why Proxy Instead of Same Port?
? **Same Port Approach**:
- Cannot run two servers on same port
- Would need complex routing configuration
- Frontend and backend tightly coupled

? **Proxy Approach**:
- Both servers run independently
- No CORS issues (same-origin from browser perspective)
- Hot reload works for both
- Clean separation of concerns
- Matches production architecture

## Troubleshooting
**Proxy not working?**
1. Check both servers are running
2. Verify `proxy.conf.json` exists in `app/` folder
3. Restart Angular dev server: `Ctrl+C` then `ng serve`

**Still seeing CORS errors?**
- You're probably accessing API directly (https://localhost:5001)
- Use http://localhost:4200 instead

## Production Deployment
In production, Netlify serves the Angular app and makes CORS-enabled requests to Railway API.
No proxy needed because they're on different domains with CORS configured.
