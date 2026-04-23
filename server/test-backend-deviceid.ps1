# Simple Test - Does Backend Return DeviceId?

Write-Host ""
Write-Host "Testing if backend returns deviceId..." -ForegroundColor Cyan
Write-Host ""

try {
    $loginBody = @{
        email = "patient@example.com"
        password = "Patient123!"
    } | ConvertTo-Json
    
    $response = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" `
        -Method Post `
        -Body $loginBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    
    if ($response.deviceId) {
        Write-Host "? SUCCESS!" -ForegroundColor Green
        Write-Host "Backend IS returning deviceId: $($response.deviceId)" -ForegroundColor Green
        Write-Host ""
        Write-Host "The fix is ACTIVE. If Angular still fails:" -ForegroundColor Yellow
        Write-Host "1. Restart Angular dev server" -ForegroundColor White
        Write-Host "2. Clear browser cache (Ctrl+Shift+Delete)" -ForegroundColor White
        Write-Host "3. Hard refresh (Ctrl+F5)" -ForegroundColor White
    } else {
        Write-Host "? FAILED!" -ForegroundColor Red
        Write-Host "Backend is NOT returning deviceId" -ForegroundColor Red
        Write-Host ""
        Write-Host "This means backend is running OLD CODE" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "FIX:" -ForegroundColor Yellow
        Write-Host "1. Stop debugger in Visual Studio (Shift+F5)" -ForegroundColor White
        Write-Host "2. Kill all dotnet processes:" -ForegroundColor White
        Write-Host "   Get-Process -Name 'dotnet' | Stop-Process -Force" -ForegroundColor Gray
        Write-Host "3. Rebuild:" -ForegroundColor White
        Write-Host "   cd server; dotnet build" -ForegroundColor Gray
        Write-Host "4. Run:" -ForegroundColor White
        Write-Host "   dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "Full Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor Gray
    
} catch {
    Write-Host "? ERROR!" -ForegroundColor Red
    Write-Host "Backend is not responding or login failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Make sure backend is running:" -ForegroundColor Yellow
    Write-Host "cd server" -ForegroundColor White
    Write-Host "dotnet run --project Propel.Api.Gateway\Propel.Api.Gateway.csproj" -ForegroundColor White
}

Write-Host ""
