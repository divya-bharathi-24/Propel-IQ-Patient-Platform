# ? Angular Proxy Configuration - Complete Summary

## What Was Changed

### Problem
You wanted Angular and .NET to run on the same port to avoid CORS issues.

### Solution
**Running on the same port is not possible** (two processes can't bind to the same port). Instead, we implemented the **Angular Proxy** pattern, which achieves the same goal: **no CORS issues!**

---

## Files Created/Modified

### 1. **Created: `app/proxy.conf.json`**
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
  },
  "/swagger": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true
  }
}
```

**What it does**: Tells Angular dev server to forward any request matching `/api`, `/health`, `/healthz`, or `/swagger` to `https://localhost:5001`.

---

### 2. **Modified: `app/angular.json`**
```json
"serve": {
  "configurations": {
    "development": {
      "buildTarget": "propel-iq-patient-platform:build:development",
      "proxyConfig": "proxy.conf.json"  // ? Added this
    }
  }
}
```

**What it does**: Tells Angular dev server to use the proxy configuration when running in development mode.

---

### 3. **Modified: `app/src/assets/env.js`**
```javascript
window.__env = {
  apiUrl: ""  // ? Changed from "https://localhost:5001" to ""
};
```

**What it does**: 
- Empty string (`""`) makes Angular use **relative URLs**
- Request to `/api/Auth/register` goes to the same origin (Angular dev server)
- Angular dev server proxies it to `https://localhost:5001/api/Auth/register`

---

### 4. **Modified: `app/package.json`**
```json
"scripts": {
  "start": "ng serve",
  "start:proxy": "ng serve --proxy-config proxy.conf.json"  // ? Added this
}
```

**What it does**: Provides an explicit script to start Angular with proxy (though `start` also uses proxy in development mode).

---

### 5. **Created: `start-dev.ps1`**
PowerShell script that starts both servers automatically.

---

### 6. **Created: `stop-dev.ps1`**
PowerShell script that stops both servers (kills processes on ports 4200, 5000, 5001).

---

### 7. **Updated: `STARTUP_GUIDE.md`**
Comprehensive guide with proxy architecture explanation.

---

### 8. **Created: `QUICK_START.md`**
Quick reference card for daily development.

---

## How It Works

### Request Flow

```
1. Browser makes request to:
   http://localhost:4200/api/Auth/register

2. Angular dev server receives the request

3. Matches proxy rule for "/api"

4. Forwards to: https://localhost:5001/api/Auth/register

5. .NET API processes and responds

6. Angular dev server returns response to browser

7. Browser sees it as same-origin (no CORS check!)
```

### Visual Architecture

```
????????????????????????????
?   Browser                ?
?   localhost:4200         ?
????????????????????????????
          ?
          ? All requests appear same-origin
          ?
????????????????????????????
? Angular Dev Server       ?
? Port: 4200               ?
? • Serves Angular files   ?
? • Proxies /api/* to API  ?
????????????????????????????
          ?
          ? Proxied requests (no CORS)
          ?
????????????????????????????
? .NET API                 ?
? Port: 5001 (HTTPS)       ?
? Port: 5000 (HTTP)        ?
????????????????????????????
```

---

## Benefits

? **No CORS Issues**: Browser sees all requests as same-origin  
? **Independent Servers**: Both can restart independently  
? **Hot Reload**: Works for both frontend and backend  
? **Clean Separation**: Frontend and backend are decoupled  
? **Production-Ready**: Mimics production architecture  
? **Developer Friendly**: Easy to debug and test  

---

## Usage

### Option 1: Use Scripts (Recommended)

Start both servers:
```powershell
.\start-dev.ps1
```

Stop both servers:
```powershell
.\stop-dev.ps1
```

### Option 2: Manual Start

**Terminal 1 - API:**
```powershell
cd server\Propel.Api.Gateway
dotnet run --launch-profile https
```

**Terminal 2 - Angular:**
```powershell
cd app
ng serve
```
OR
```powershell
npm start
```

---

## Testing

### From Browser
1. Open http://localhost:4200
2. Navigate to registration page
3. Fill and submit form
4. Request goes through proxy automatically

### From PowerShell
```powershell
# Through proxy (no CORS issues)
curl http://localhost:4200/api/Auth/register -Method POST -ContentType "application/json" -Body '{"name":"Test","email":"test@example.com","password":"Test@123","phone":"1234567890","dateOfBirth":"2000-01-01"}'

# Direct to API (CORS headers needed)
curl https://localhost:5001/api/Auth/register -Method POST -ContentType "application/json" -Body '{"name":"Test","email":"test@example.com","password":"Test@123","phone":"1234567890","dateOfBirth":"2000-01-01"}' -SkipCertificateCheck
```

---

## Verification

After starting both servers, check for these indicators:

### Angular Terminal Should Show:
```
[HPM] Proxy created: /api  -> https://localhost:5001
[HPM] Proxy created: /health  -> https://localhost:5001
[HPM] Proxy created: /healthz  -> https://localhost:5001

** Angular Live Development Server is listening on localhost:4200 **
```

### .NET Terminal Should Show:
```
[Startup] Migrations applied successfully.
Now listening on: https://localhost:5001
Now listening on: http://localhost:5000
```

---

## Troubleshooting

### Proxy Not Working

**Symptoms**: Requests still go directly to localhost:5001  
**Solution**:
1. Check `proxy.conf.json` exists in `app/` folder
2. Check `angular.json` has `"proxyConfig": "proxy.conf.json"`
3. Restart Angular dev server (proxy config is loaded at startup)

### Still Seeing CORS Errors

**Symptoms**: CORS error in browser console  
**Solution**:
1. Make sure you're accessing http://localhost:4200 (not 5001)
2. Make sure `env.js` has `apiUrl: ""`
3. Check Angular terminal shows `[HPM] Proxy created`

### Port Already in Use

**Symptoms**: `Error: listen EADDRINUSE: address already in use`  
**Solution**:
```powershell
.\stop-dev.ps1
```
OR manually:
```powershell
netstat -ano | findstr :4200  # Find PID
taskkill /PID <PID> /F         # Kill process
```

---

## Production Deployment

In production:
- **Netlify** serves Angular static files
- Angular makes requests to **Railway API** URL (different domain)
- **CORS is enabled** on Railway API for Netlify domain
- No proxy needed (proxy is development-only)

Production `env.js` will have:
```javascript
window.__env = {
  apiUrl: "https://api.propeliq.com"  // Railway API URL
};
```

---

## Comparison: Same Port vs Proxy

| Aspect | Same Port (Not Possible) | Proxy (Our Solution) |
|--------|-------------------------|----------------------|
| Technical Feasibility | ? Cannot bind two processes to same port | ? Both processes run independently |
| CORS Issues | ? No CORS (same origin) | ? No CORS (proxied = same origin) |
| Hot Reload | ? Complex setup | ? Works out of the box |
| Development Speed | ? Slower (complex routing) | ? Fast and simple |
| Production Similarity | ? Different from production | ? Mimics production architecture |
| Debugging | ? Hard to debug | ? Easy to debug |
| Industry Standard | ? Not recommended | ? Standard practice |

---

## Next Steps

1. ? Run `.\start-dev.ps1` to start both servers
2. ? Open http://localhost:4200 in browser
3. ? Test registration functionality
4. ? Verify no CORS errors in console

---

## Additional Resources

- [Angular Proxy Configuration Docs](https://angular.io/guide/build#proxying-to-a-backend-server)
- [webpack-dev-server Proxy Docs](https://webpack.js.org/configuration/dev-server/#devserverproxy)
- CORS Best Practices: Keep CORS enabled on backend for production, use proxy for development

---

**Questions or Issues?**
Check `STARTUP_GUIDE.md` or `QUICK_START.md` for detailed instructions.
