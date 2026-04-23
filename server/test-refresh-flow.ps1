# Test Refresh Flow with Debug Logging

Write-Host "=== REFRESH FLOW DEBUG TEST ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 1: Testing if backend returns deviceId..." -ForegroundColor Yellow
Write-Host ""

try {
    $loginBody = @{
        email = "patient@example.com"
        password = "Patient123!"
    } | ConvertTo-Json
    
    $loginResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" `
        -Method Post `
        -Body $loginBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    if ($loginResponse.deviceId) {
        Write-Host "? Backend returns deviceId: $($loginResponse.deviceId)" -ForegroundColor Green
        $global:deviceId = $loginResponse.deviceId
        $global:accessToken = $loginResponse.accessToken
        $global:refreshToken = $loginResponse.refreshToken
    } else {
        Write-Host "? Backend NOT returning deviceId!" -ForegroundColor Red
        exit 1
    }
    
} catch {
    Write-Host "? Login failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Step 2: Testing refresh endpoint..." -ForegroundColor Yellow
Write-Host ""

try {
    $refreshBody = @{
        refreshToken = $global:refreshToken
        deviceId = $global:deviceId
    } | ConvertTo-Json
    
    Write-Host "Calling refresh with:" -ForegroundColor Gray
    Write-Host "  refreshToken: $($global:refreshToken.Substring(0, 20))..." -ForegroundColor Gray
    Write-Host "  deviceId: $($global:deviceId)" -ForegroundColor Gray
    Write-Host ""
    
    $refreshResponse = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/refresh" `
        -Method Post `
        -Body $refreshBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    Write-Host "? Refresh successful!" -ForegroundColor Green
    Write-Host "  New accessToken: $($refreshResponse.accessToken.Substring(0, 20))..." -ForegroundColor Green
    Write-Host "  New deviceId: $($refreshResponse.deviceId)" -ForegroundColor Green
    
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    Write-Host "? Refresh failed: HTTP $statusCode" -ForegroundColor Red
    
    try {
        $errorBody = $_.ErrorDetails.Message | ConvertFrom-Json
        Write-Host "  Error: $($errorBody.error)" -ForegroundColor Red
    } catch {
        Write-Host "  Raw error: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "NOW CHECK THE BACKEND CONSOLE LOGS!" -ForegroundColor Yellow
    Write-Host "================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Look for these debug messages:" -ForegroundColor White
    Write-Host ""
    Write-Host "[SESSION DEBUG] Session SET - Key: session:..." -ForegroundColor Cyan
    Write-Host "  ^^ This shows what session was created during login" -ForegroundColor Gray
    Write-Host ""
    Write-Host "[REFRESH DEBUG] Starting refresh. DeviceId: ..." -ForegroundColor Cyan
    Write-Host "  ^^ This shows what deviceId Angular sent" -ForegroundColor Gray
    Write-Host ""
    Write-Host "[SESSION DEBUG] Session EXISTS check - Key: ..." -ForegroundColor Cyan
    Write-Host "  ^^ This shows what session key we're looking for" -ForegroundColor Gray
    Write-Host ""
    Write-Host "[SESSION DEBUG] Session NOT FOUND - Available keys: ..." -ForegroundColor Cyan
    Write-Host "  ^^ This shows what sessions actually exist" -ForegroundColor Gray
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The logs will show EXACTLY why the session doesn't match!" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== TEST COMPLETE ===" -ForegroundColor Cyan
Write-Host ""
