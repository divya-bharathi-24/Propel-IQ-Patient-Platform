# PowerShell script to start Redis using Docker
# This will start a Redis container on port 6379

Write-Host "Starting Redis container..." -ForegroundColor Yellow

# Check if Docker is running
try {
    docker version | Out-Null
} catch {
    Write-Host "Error: Docker is not running. Please start Docker Desktop first." -ForegroundColor Red
    exit 1
}

# Check if Redis container already exists
$existingContainer = docker ps -a --filter "name=propeliq-redis" --format "{{.Names}}"

if ($existingContainer -eq "propeliq-redis") {
    Write-Host "Redis container already exists. Starting it..." -ForegroundColor Cyan
    docker start propeliq-redis
} else {
    Write-Host "Creating and starting new Redis container..." -ForegroundColor Cyan
    docker run -d `
        --name propeliq-redis `
        -p 6379:6379 `
        redis:7-alpine
}

# Wait a moment for Redis to start
Start-Sleep -Seconds 2

# Test the connection
Write-Host "Testing Redis connection..." -ForegroundColor Yellow
docker exec propeliq-redis redis-cli ping

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n? Redis is running on localhost:6379" -ForegroundColor Green
    Write-Host "You can now start your application." -ForegroundColor Green
} else {
    Write-Host "`n? Redis failed to start properly" -ForegroundColor Red
    Write-Host "Check logs with: docker logs propeliq-redis" -ForegroundColor Yellow
}
