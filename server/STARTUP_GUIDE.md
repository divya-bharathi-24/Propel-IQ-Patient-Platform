# Propel IQ - Local Development Startup Guide

## Prerequisites

Before starting, ensure you have:
- ? PostgreSQL running on port 5434 (or your configured port)
- ? Redis running on port 6379 (or your configured port)
- ? .NET 10 SDK installed
- ? Node.js 20+ installed

## Development Architecture

We use an **Angular Proxy** configuration to avoid CORS issues in development. This means:
- Angular dev server runs on **http://localhost:4200**
- .NET API runs on **https://localhost:5001**
- Angular proxy forwards `/api/*` requests to the .NET API
- From the browser's perspective, everything comes from port 4200 (no CORS needed!)

## Step-by-Step Startup Instructions

### 1. Start Backend API

Open a **NEW terminal window** in VS Code:

```powershell
# Navigate to the API project
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\server\Propel.Api.Gateway

# Run the API on HTTPS
dotnet run --launch-profile https
```

**Expected Output:**
```
[Startup] Migrations applied successfully.
Now listening on: https://localhost:5001
Now listening on: http://localhost:5000
Application started. Press Ctrl+C to shut down.
```

**Verify the API is running:**
- Open browser: https://localhost:5001/swagger
- You should see the Swagger UI

### 2. Start Frontend (Angular with Proxy)

Open **ANOTHER terminal window** in VS Code:

```powershell
# Navigate to the Angular app
cd D:\Propel_IQ\Propel-IQ-Patient-Platform\app

# Start Angular dev server with proxy configuration
ng serve
```

**Expected Output:**
```
? Browser application bundle generation complete.

[HPM] Proxy created: /api  -> https://localhost:5001
[HPM] Proxy created: /health  -> https://localhost:5001
[HPM] Proxy created: /healthz  -> https://localhost:5001

** Angular Live Development Server is listening on localhost:4200, open your browser on http://localhost:4200/ **
```

**Verify Angular is running:**
- Open browser: http://localhost:4200
- You should see your Angular application

### 3. Test the Registration Endpoint

Now with the proxy, all API requests go through port 4200:

#### Option A: From Angular UI (Recommended)
1. Navigate to: http://localhost:4200/auth/register
2. Fill in the registration form
3. Submit
4. The Angular app will make a request to `http://localhost:4200/api/Auth/register`
5. The Angular proxy will forward it to `https://localhost:5001/api/Auth/register`
6. **No CORS issues!** ?

#### Option B: Test Proxy Directly

```powershell
# Request to Angular dev server (proxy will forward to API)
curl.exe -X POST http://localhost:4200/api/Auth/register `
  -H "Content-Type: application/json" `
  -H "Accept: application/json" `
  -d '{\"name\":\"Jothish\",\"email\":\"jothis11@gmail.com\",\"password\":\"Jothis@10\",\"phone\":\"7598545942\",\"dateOfBirth\":\"2013-07-15\"}'
```

#### Option C: Test API Directly (Still Works)

```powershell
# Direct request to .NET API
curl.exe -X POST https://localhost:5001/api/Auth/register `
  -H "Origin: http://localhost:4200" `
  -H "Content-Type: application/json" `
  -H "Accept: application/json" `
  -d '{\"name\":\"Jothish\",\"email\":\"jothis11@gmail.com\",\"password\":\"Jothis@10\",\"phone\":\"7598545942\",\"dateOfBirth\":\"2013-07-15\"}' `
  --insecure
```

---

## Architecture Overview

### Development (with Angular Proxy)

```
???????????????????????????????????????????????
?  Browser                                    ?
?  All requests to http://localhost:4200     ?
???????????????????????????????????????????????
               ?
               ?
???????????????????????????????????????????????
?  Angular Dev Server (Port 4200)             ?
?  - Serves Angular app                       ?
?  - Proxies /api/* ? https://localhost:5001  ?
?  - NO CORS ISSUES! ?                        ?
???????????????????????????????????????????????
               ?
               ? Proxied requests
               ?
???????????????????????????????????????????????
?  .NET API Gateway                           ?
?  https://localhost:5001                     ?
?  http://localhost:5000                      ?
???????????????????????????????????????????????
               ?
               ??????????????????????????????????
               ?                 ?              ?
               ?                 ?              ?
         ???????????       ???????????   ???????????
         ?PostgreSQL?       ?  Redis  ?   ?SendGrid ?
         ?   :5434  ?       ?  :6379  ?   ?  (SMTP) ?
         ???????????       ???????????   ???????????
```

### Production (Deployed)

```
???????????????????????????????????????????????
?  Browser                                    ?
?  All requests to https://propeliq.app       ?
???????????????????????????????????????????????
               ?
               ??????????????????????????????????
               ?                  ?             ?
               ?                  ?             ?
    ????????????????    ????????????????       ?
    ?  Netlify CDN ?    ?  Railway API ?       ?
    ?  (Frontend)  ?    ?  (Backend)   ?       ?
    ????????????????    ????????????????       ?
                         CORS enabled           ?
```

---

## How the Proxy Works

1. Your Angular code makes a request to: `${apiUrl}/api/Auth/register`
2. Since `apiUrl = ""` (empty), the request goes to: `http://localhost:4200/api/Auth/register`
3. Angular dev server sees the `/api` prefix
4. It matches the proxy rule in `proxy.conf.json`
5. It forwards the request to: `https://localhost:5001/api/Auth/register`
6. The .NET API responds
7. Angular proxy returns the response to your browser
8. **No CORS check happens** because from the browser's perspective, everything is same-origin!

---

## Troubleshooting

### Issue: "Port already in use"

**For port 5001 (API):**
```powershell
# Find process using port 5001
netstat -ano | findstr :5001

# Kill the process (replace PID with actual process ID)
taskkill /PID <PID> /F
```

**For port 4200 (Angular):**
```powershell
# Find process using port 4200
netstat -ano | findstr :4200

# Kill the process
taskkill /PID <PID> /F
```

### Issue: "SSL Certificate not trusted"

Run this command once:
```powershell
dotnet dev-certs https --trust
```

### Issue: "Proxy error: Could not proxy request"

This usually means the .NET API is not running. Verify:
1. ? The API is actually running on https://localhost:5001
2. ? Check the API terminal for errors
3. ? Test direct access: https://localhost:5001/swagger

### Issue: "404 Not Found" when calling API through proxy

**Check these things:**
1. ? `proxy.conf.json` exists in the `app/` directory
2. ? `angular.json` has `"proxyConfig": "proxy.conf.json"` in the development configuration
3. ? Restart the Angular dev server after creating/modifying proxy config
4. ? Check the Angular dev server console for proxy logs (should show `[HPM] Proxy created: /api`)

### Issue: Proxy not working after code changes

The proxy configuration is read at startup. After changing `proxy.conf.json`:
```powershell
# Stop Angular dev server (Ctrl+C)
# Restart it
ng serve
```

---

## Quick Start (Both Servers)

PowerShell script to start both servers:

```powershell
# Save this as start-dev.ps1

# Start API in a new window
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd 'D:\Propel_IQ\Propel-IQ-Patient-Platform\server\Propel.Api.Gateway'; dotnet run --launch-profile https"

# Wait 5 seconds for API to start
Start-Sleep -Seconds 5

# Start Angular with proxy in a new window
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd 'D:\Propel_IQ\Propel-IQ-Patient-Platform\app'; ng serve"

Write-Host "? Starting both servers..."
Write-Host "?? API: https://localhost:5001 (direct access)"
Write-Host "?? Frontend: http://localhost:4200 (with proxy to API)"
Write-Host "?? Proxy: http://localhost:4200/api/* ? https://localhost:5001/api/*"
```

Then run:
```powershell
.\start-dev.ps1
```

---

## Health Checks

### Through Proxy (Same Origin)
- **Proxied API Health:** http://localhost:4200/health
- **Proxied Detailed Health:** http://localhost:4200/healthz

### Direct to API
- **API Health:** https://localhost:5001/health
- **Detailed Health:** https://localhost:5001/healthz
- **Swagger UI:** https://localhost:5001/swagger

---

## Configuration Files

### Angular Proxy (`app/proxy.conf.json`)
```json
{
  "/api": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true,
    "logLevel": "debug"
  },
  "/health": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true
  },
  "/healthz": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true
  }
}
```

### Backend CORS (Still Configured for Production)

`appsettings.Development.json`:
```json
{
  "CORS": {
    "AllowedOrigins": "http://localhost:4200,https://localhost:4200,http://localhost:3000,https://localhost:3000"
  }
}
```

> **Note**: In development with proxy, CORS is not needed since all requests appear to come from localhost:4200. However, we keep the CORS configuration for:
> - Testing the API directly (without proxy)
> - Production deployment (Netlify ? Railway)

### Frontend Environment (`app/src/assets/env.js`)
```javascript
window.__env = {
  // Empty string uses same origin (proxied)
  apiUrl: ""
};
```

---

## Benefits of This Approach

? **No CORS issues in development** - All requests are same-origin  
? **Hot reload works** - Both frontend and backend can reload independently  
? **Realistic development** - Mimics how it works in production (CDN ? API)  
? **Easy debugging** - Clear separation of concerns  
? **Secure** - API still uses HTTPS  
? **Flexible** - Can still access API directly at https://localhost:5001

---

**Happy Coding! ??**
