# ?? Quick Action: Restart Application

## What Was Fixed
The `SessionExpirySubscriberService` required Redis, which is disabled in development. This service is now automatically disabled in development mode.

## What You Need to Do RIGHT NOW

### Step 1: Stop the Current Debug Session
Press **Shift+F5** in Visual Studio

### Step 2: Start Debugging Again
Press **F5** in Visual Studio

### Step 3: Check the Output Window
You should see these messages (ALL NORMAL):
```
[WARN] DEVELOPMENT MODE: Redis is disabled. Using IN-MEMORY session storage. Sessions will be lost on restart!
[INFO] Session service: Using IN-MEMORY storage (development mode)
[WARN] SessionExpirySubscriberService: DISABLED (development mode - Redis not available)
[INFO] Data Protection: Persisting keys to ...
[Startup] Migrations applied successfully.
```

### Step 4: Verify Application Started
- Check that Swagger UI loads: `https://localhost:7213/swagger`
- No errors in the Output window

## That's It! 

Your application should now start successfully without any Redis errors.

---

## After Application Starts Successfully

### One-Time Database Setup
Apply the migration for the `patient_id` column:

1. Open **pgAdmin** or **DBeaver**
2. Connect to `propeliq_dev` database (localhost:5434)
3. Open and execute: `add_patient_id_to_refresh_tokens.sql`

### Test Login
1. Go to Swagger: `https://localhost:7213/swagger`
2. Expand `/api/auth/login`
3. Click "Try it out"
4. Use this request:
```json
{
  "email": "your-patient-email@example.com",
  "password": "YourPassword123!",
  "deviceId": "test-device"
}
```
5. Click "Execute"
6. You should get tokens back!

---

## Troubleshooting

### If app still crashes:
1. Close Visual Studio
2. Delete `bin` and `obj` folders:
   ```powershell
   Get-ChildItem -Recurse -Directory -Filter bin | Remove-Item -Recurse -Force
   Get-ChildItem -Recurse -Directory -Filter obj | Remove-Item -Recurse -Force
   ```
3. Reopen Visual Studio
4. Press F5

### If you see Redis errors:
Make sure you saved `Program.cs` before restarting. Check that the file contains:
```csharp
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<SessionExpirySubscriberService>();
```

---

**Current Status:** ? Fix Applied  
**Next Action:** Restart Application (Shift+F5, then F5)  
**Expected Result:** Clean startup with warnings (normal)
