# Test Dashboard API with detailed logging
param(
    [string]$baseUrl = "https://localhost:5001"
)

Write-Host "=== Testing Dashboard API Flow ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Login
Write-Host "1. Logging in..." -ForegroundColor Yellow
$loginPayload = @{
    email = "test.patient@example.com"
    password = "SecurePass123!"
    deviceId = "test-device-123"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-RestMethod `
        -Uri "$baseUrl/api/auth/login" `
        -Method POST `
        -Body $loginPayload `
        -ContentType "application/json" `
        -SkipCertificateCheck
    
    Write-Host "? Login successful!" -ForegroundColor Green
    Write-Host "   UserId: $($loginResponse.userId)" -ForegroundColor Gray
    Write-Host "   Role: $($loginResponse.role)" -ForegroundColor Gray
    Write-Host "   DeviceId: $($loginResponse.deviceId)" -ForegroundColor Gray
    Write-Host ""
    
    $token = $loginResponse.accessToken
    
    # Step 2: Immediately call dashboard (session should exist)
    Write-Host "2. Calling dashboard API..." -ForegroundColor Yellow
    $headers = @{
        Authorization = "Bearer $token"
    }
    
    $dashboardResponse = Invoke-RestMethod `
        -Uri "$baseUrl/api/patient/dashboard" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck
    
    Write-Host "? Dashboard call successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Dashboard Data:" -ForegroundColor Cyan
    $dashboardResponse | ConvertTo-Json -Depth 10
    
} catch {
    Write-Host "? Error occurred!" -ForegroundColor Red
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "Error Message: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response Body: $responseBody" -ForegroundColor Yellow
    }
}
