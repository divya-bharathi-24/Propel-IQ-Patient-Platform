# Redis Session Management Fix

## Problem
The application failed with `RedisConnectionException` because Redis is not running locally at `localhost:6379`.

## Solution Options

### Option 1: Start Redis Locally (Recommended)

#### Using Docker (Easiest)
1. Make sure Docker Desktop is running
2. Run the provided script:
   ```powershell
   .\start-redis.ps1
   ```

This will start a Redis container on port 6379.

#### Using WSL2/Linux
If you have Redis installed via WSL2:
```bash
redis-server
```

#### Using Windows Redis
Download and install Redis for Windows from:
https://github.com/microsoftarchive/redis/releases

Then start it:
```powershell
redis-server
```

### Option 2: Use In-Memory Session Storage (Development Only)

I've modified the application to automatically fall back to in-memory session storage when Redis is unavailable **in Development mode only**.

**Pros:**
- No Redis installation required for local development
- Faster startup

**Cons:**
- Sessions are lost on application restart
- Not suitable for multi-instance deployments
- NOT available in Production (application will fail if Redis is unavailable)

The application will automatically detect Redis availability and:
- ? Use Redis if available (recommended)
- ? Fall back to in-memory if Redis is unavailable (Development only)
- ? Throw error if Redis is unavailable in Production

## Verification

After starting Redis (Option 1) or relying on fallback (Option 2), check the application logs:

**Redis Connected:**
```
[INFO] Redis connected successfully at localhost:6379
```

**Using Fallback:**
```
[WARN] Redis connection failed at localhost:6379. Falling back to in-memory session storage.
[WARN] Using IN-MEMORY session storage. Sessions will be lost on restart!
```

## Testing Redis Connection

### Using redis-cli (if Redis is running):
```bash
redis-cli ping
# Should return: PONG
```

### Using Docker:
```powershell
docker exec propeliq-redis redis-cli ping
# Should return: PONG
```

## Docker Compose Setup (Future)

For a complete local development environment, consider using Docker Compose:

```yaml
# docker-compose.yml
version: '3.8'
services:
  postgres:
    image: postgres:17-alpine
    ports:
      - "5434:5432"
    environment:
      POSTGRES_DB: propeliq_dev
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: Jothis@10
    volumes:
      - postgres-data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data

volumes:
  postgres-data:
  redis-data:
```

Start both services:
```powershell
docker-compose up -d
```

## Configuration Files Updated

1. **Propel.Api.Gateway/Program.cs** - Added fallback logic for Redis connection
2. **Propel.Modules.Auth/Services/InMemoryRedisSessionService.cs** - New in-memory session service
3. **start-redis.ps1** - Helper script to start Redis using Docker

## Next Steps

1. **Recommended:** Run `.\start-redis.ps1` to start Redis
2. Restart your application
3. Try logging in again - it should work

If you continue to have issues, check:
- Docker Desktop is running
- Port 6379 is not already in use: `netstat -an | findstr 6379`
- Redis container is running: `docker ps | findstr redis`
