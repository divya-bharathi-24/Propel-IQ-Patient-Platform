# Quick Diagnostic - Check Current State

Write-Host "=== CURRENT STATE DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host ""

# Check if backend is running
Write-Host "1. Checking backend status..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get -TimeoutSec 3
    Write-Host "   ? Backend is running" -ForegroundColor Green
    
    # Test login endpoint with actual credentials
    Write-Host ""
    Write-Host "2. Testing login endpoint..." -ForegroundColor Yellow
    Write-Host "   (This will attempt login - check backend logs)" -ForegroundColor Gray
    
    $loginBody = @{
        email = "patient@example.com"
        password = "Patient123!"
    } | ConvertTo-Json
    
    try {
        $loginResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" `
            -Method Post `
            -Body $loginBody `
            -ContentType "application/json" `
            -TimeoutSec 5
        
        Write-Host "   ? Login successful" -ForegroundColor Green
        Write-Host ""
        Write-Host "   Login Response:" -ForegroundColor Cyan
        Write-Host "   =================" -ForegroundColor Cyan
        
        # Check each field
        if ($loginResponse.accessToken) {
            Write-Host "   ? accessToken: present (${($loginResponse.accessToken.Length)} chars)" -ForegroundColor Green
        } else {
            Write-Host "   ? accessToken: MISSING" -ForegroundColor Red
        }
        
        if ($loginResponse.refreshToken) {
            Write-Host "   ? refreshToken: present" -ForegroundColor Green
        } else {
            Write-Host "   ? refreshToken: MISSING" -ForegroundColor Red
        }
        
        if ($loginResponse.userId) {
            Write-Host "   ? userId: $($loginResponse.userId)" -ForegroundColor Green
        } else {
            Write-Host "   ? userId: MISSING" -ForegroundColor Red
        }
        
        if ($loginResponse.role) {
            Write-Host "   ? role: $($loginResponse.role)" -ForegroundColor Green
        } else {
            Write-Host "   ? role: MISSING" -ForegroundColor Red
        }
        
        if ($loginResponse.deviceId) {
            Write-Host "   ? deviceId: $($loginResponse.deviceId)" -ForegroundColor Green
            $global:testDeviceId = $loginResponse.deviceId
        } else {
            Write-Host "   ? deviceId: MISSING ? THIS IS THE PROBLEM!" -ForegroundColor Red
        }
        
        Write-Host ""
        Write-Host "   Full Response JSON:" -ForegroundColor Cyan
        $loginResponse | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor Gray
        
        # Save token for dashboard test
        $global:testToken = $loginResponse.accessToken
        
        # Test dashboard with token
        if ($global:testToken) {
            Write-Host ""
            Write-Host "3. Testing dashboard endpoint..." -ForegroundColor Yellow
            
            $headers = @{
                "Authorization" = "Bearer $global:testToken"
            }
            
            try {
                $dashboardResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/patient/dashboard" `
                    -Method Get `
                    -Headers $headers `
                    -TimeoutSec 5
                
                Write-Host "   ? Dashboard call successful!" -ForegroundColor Green
                Write-Host "   Dashboard returned data for patient" -ForegroundColor Green
                
            } catch {
                $statusCode = $_.Exception.Response.StatusCode.Value__
                Write-Host "   ? Dashboard call failed: HTTP $statusCode" -ForegroundColor Red
                
                if ($statusCode -eq 401) {
                    Write-Host ""
                    Write-Host "   ERROR ANALYSIS:" -ForegroundColor Yellow
                    Write-Host "   ===============" -ForegroundColor Yellow
                    
                    try {
                        $errorBody = $_.ErrorDetails.Message | ConvertFrom-Json
                        if ($errorBody.error -eq "session_expired") {
                            Write-Host "   Error: session_expired" -ForegroundColor Red
                            Write-Host ""
                            Write-Host "   This means:" -ForegroundColor White
                            Write-Host "   1. JWT is valid" -ForegroundColor Gray
                            Write-Host "   2. BUT Redis session not found" -ForegroundColor Gray
                            Write-Host "   3. Device ID mismatch between JWT and session" -ForegroundColor Gray
                            Write-Host ""
                            Write-Host "   LIKELY CAUSE:" -ForegroundColor Yellow
                            Write-Host "   - Backend code changes not active yet" -ForegroundColor White
                            Write-Host "   - Need to STOP debugger and rebuild" -ForegroundColor White
                        }
                    } catch {
                        Write-Host "   Could not parse error response" -ForegroundColor Gray
                    }
                }
            }
        }
        
    } catch {
        Write-Host "   ? Login failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    
} catch {
    Write-Host "   ? Backend is NOT running!" -ForegroundColor Red
    Write-Host "   Start it with: dotnet run --project server\Propel.Api.Gateway" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== DIAGNOSTIC COMPLETE ===" -ForegroundColor Cyan
Write-Host ""

# Check if Visual Studio debugger is running
$vsProcess = Get-Process -Name "devenv" -ErrorAction SilentlyContinue
if ($vsProcess) {
    Write-Host "? WARNING: Visual Studio is running" -ForegroundColor Yellow
    Write-Host "  If debugger is active, code changes are NOT applied!" -ForegroundColor Yellow
    Write-Host "  You must:" -ForegroundColor Yellow
    Write-Host "  1. Stop debugging (Shift+F5)" -ForegroundColor White
    Write-Host "  2. Run: .\restart-all.ps1" -ForegroundColor White
}

Write-Host ""
Write-Host "=== SOLUTION ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "If deviceId is MISSING:" -ForegroundColor Yellow
Write-Host "  1. Stop ALL running processes" -ForegroundColor White
Write-Host "  2. Run: .\restart-all.ps1" -ForegroundColor Green
Write-Host "  3. Test again" -ForegroundColor White
Write-Host ""
