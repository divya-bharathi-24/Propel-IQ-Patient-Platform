# ?? Propel IQ - Development Environment Setup Complete

## ? What's Been Configured

Your development environment now uses an **Angular Proxy** to eliminate CORS issues. This is the **industry-standard approach** for Angular + backend API development.

---

## ?? Documentation Files

| File | Purpose |
|------|---------|
| **`QUICK_START.md`** | Quick reference for daily development (? Start here!) |
| **`STARTUP_GUIDE.md`** | Comprehensive setup and troubleshooting guide |
| **`PROXY_SETUP_SUMMARY.md`** | Detailed explanation of the proxy configuration |
| **`start-dev.ps1`** | PowerShell script to start both servers automatically |
| **`stop-dev.ps1`** | PowerShell script to stop all development servers |

---

## ?? Quick Start (TL;DR)

### Automated Start
```powershell
.\start-dev.ps1
```

### Manual Start

**Terminal 1:**
```powershell
cd server\Propel.Api.Gateway
dotnet run --launch-profile https
```

**Terminal 2:**
```powershell
cd app
ng serve
```

### Access Your App
- Frontend: **http://localhost:4200**
- API Swagger: **https://localhost:5001/swagger**

---

## ?? How It Works

Instead of running both on the same port (which is impossible), we use Angular's proxy feature:

```
Browser ? http://localhost:4200/api/Auth/register
         ? (Angular Proxy)
         https://localhost:5001/api/Auth/register
         ?
         Response (No CORS! ?)
```

**From the browser's perspective**: Everything comes from `localhost:4200` (same-origin)  
**Behind the scenes**: Angular dev server forwards API requests to `localhost:5001`  
**Result**: No CORS configuration needed in development!

---

## ?? Configuration Files

### `app/proxy.conf.json` (New)
```json
{
  "/api": {
    "target": "https://localhost:5001",
    "secure": false,
    "changeOrigin": true
  }
}
```

### `app/angular.json` (Modified)
```json
"development": {
  "proxyConfig": "proxy.conf.json"
}
```

### `app/src/assets/env.js` (Modified)
```javascript
window.__env = {
  apiUrl: ""  // Empty = use same origin (proxied)
};
```

---

## ? Benefits

? **No CORS Issues** - All requests appear same-origin to browser  
? **Hot Reload** - Both servers can restart independently  
? **Clean Separation** - Frontend and backend properly decoupled  
? **Easy Debugging** - Clear request flow  
? **Production-Ready** - Mimics actual deployment architecture  

---

## ?? Common Commands

| Command | Description |
|---------|-------------|
| `.\start-dev.ps1` | Start both servers automatically |
| `.\stop-dev.ps1` | Stop all development servers |
| `ng serve` | Start Angular with proxy (development mode) |
| `npm start` | Same as `ng serve` |
| `dotnet run --launch-profile https` | Start .NET API |

---

## ?? Troubleshooting

### "Port already in use"
```powershell
.\stop-dev.ps1
```

### "Proxy error: Could not proxy request"
Make sure .NET API is running on https://localhost:5001

### "CORS error" in browser console
You're probably accessing the API directly. Use http://localhost:4200 instead.

### Proxy not forwarding requests
Restart Angular dev server (Ctrl+C, then `ng serve`)

---

## ?? Full Documentation

For detailed information, see:
- **Daily Use**: `QUICK_START.md`
- **Setup & Troubleshooting**: `STARTUP_GUIDE.md`
- **Technical Details**: `PROXY_SETUP_SUMMARY.md`

---

## ?? Why Not Run on Same Port?

**Q**: Why can't we run Angular and .NET on the same port?  
**A**: Two processes cannot bind to the same port simultaneously. It's a fundamental OS limitation.

**Q**: But I want to avoid CORS!  
**A**: The proxy achieves exactly that - no CORS issues - while keeping servers independent.

**Q**: How does this work in production?  
**A**: In production, Netlify (frontend) and Railway (backend) are on different domains. CORS is properly configured for cross-origin requests.

---

## ? Verification Checklist

After running `.\start-dev.ps1`, you should see:

- [ ] .NET API terminal shows: `Now listening on: https://localhost:5001`
- [ ] Angular terminal shows: `[HPM] Proxy created: /api -> https://localhost:5001`
- [ ] Browser at http://localhost:4200 loads your Angular app
- [ ] Browser at https://localhost:5001/swagger loads Swagger UI
- [ ] No CORS errors in browser console when using the app

---

## ?? You're All Set!

Your development environment is now configured for optimal Angular + .NET development with zero CORS issues.

**Happy Coding! ??**
