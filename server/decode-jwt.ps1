# Decode JWT token to inspect claims
param(
    [Parameter(Mandatory=$true)]
    [string]$token
)

function Decode-JwtPayload {
    param([string]$jwt)
    
    # Split the JWT
    $parts = $jwt.Split('.')
    if ($parts.Length -ne 3) {
        Write-Host "Invalid JWT format" -ForegroundColor Red
        return
    }
    
    # Get the payload (second part)
    $payload = $parts[1]
    
    # Add padding if needed (JWT Base64URL doesn't use padding)
    switch ($payload.Length % 4) {
        2 { $payload += "==" }
        3 { $payload += "=" }
    }
    
    # Replace Base64URL characters with Base64
    $payload = $payload.Replace('-', '+').Replace('_', '/')
    
    # Decode from Base64
    $bytes = [Convert]::FromBase64String($payload)
    $json = [System.Text.Encoding]::UTF8.GetString($bytes)
    
    # Parse and display
    $claims = $json | ConvertFrom-Json
    
    Write-Host "=== JWT Claims ===" -ForegroundColor Cyan
    $claims | Format-List
    
    Write-Host "`n=== Key Claims ===" -ForegroundColor Yellow
    Write-Host "UserId (sub): $($claims.sub)" -ForegroundColor Green
    Write-Host "Role: $($claims.'http://schemas.microsoft.com/ws/2008/06/identity/claims/role')" -ForegroundColor Green
    Write-Host "DeviceId: $($claims.deviceId)" -ForegroundColor Green
    Write-Host "JTI: $($claims.jti)" -ForegroundColor Green
    Write-Host "Issued At: $([DateTimeOffset]::FromUnixTimeSeconds($claims.iat).LocalDateTime)" -ForegroundColor Green
    Write-Host "Expires At: $([DateTimeOffset]::FromUnixTimeSeconds($claims.exp).LocalDateTime)" -ForegroundColor Green
    
    # Check if expired
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    if ($now -gt $claims.exp) {
        Write-Host "`n? TOKEN IS EXPIRED!" -ForegroundColor Red
    } else {
        $timeLeft = $claims.exp - $now
        Write-Host "`n? Token is valid for $timeLeft more seconds" -ForegroundColor Green
    }
}

Decode-JwtPayload -jwt $token
