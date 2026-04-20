# Task - TASK_003

## Requirement Reference

- User Story: [us_005] (extracted from input)
- Story Location: [.propel/context/tasks/EP-TECH/us_005/us_005.md]
- Acceptance Criteria:
  - **AC-3**: Given environment variables are configured in both platforms, When the application starts, Then it reads all required configuration values (DB connection, Redis URL, JWT secrets) from the environment without hardcoded fallbacks.
  - **AC-4**: Given the deployment is live, When I access the API from the Netlify-hosted frontend, Then CORS headers are correctly configured to allow requests only from the Netlify domain.
- Edge Case:
  - How are CORS misconfigurations caught? — E2E smoke test from the deployed frontend verifies a cross-origin API call succeeds (implemented here as a Playwright-based smoke check step in the CD pipeline via US_004 task_002).

## Design References (Frontend Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **UI Impact**        | No    |
| **Figma URL**        | N/A   |
| **Wireframe Status** | N/A   |
| **Wireframe Type**   | N/A   |
| **Wireframe Path/URL** | N/A |
| **Screen Spec**      | N/A   |
| **UXR Requirements** | N/A   |
| **Design Tokens**    | N/A   |

## Applicable Technology Stack

| Layer          | Technology             | Version |
| -------------- | ---------------------- | ------- |
| Backend        | ASP.NET Core Web API   | .net 10  |
| Frontend       | Angular                | 18.x    |
| Hosting (FE)   | Netlify                | —       |
| Hosting (BE)   | Railway                | —       |
| ORM/Config     | Entity Framework Core  | 9.x     |
| Cache          | Upstash Redis          | Serverless |
| Auth           | JWT                    | —       |
| AI/ML          | N/A                    | N/A     |
| Mobile         | N/A                    | N/A     |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type       | Value |
| -------------------- | ----- |
| **AI Impact**        | No    |
| **AIR Requirements** | N/A   |
| **AI Pattern**       | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config**| N/A   |
| **Model Provider**   | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Ensure the platform reads all sensitive configuration values (database connection string, Redis URL, JWT secrets) exclusively from environment variables — with no hardcoded fallbacks — across both Netlify (frontend) and Railway (backend). Additionally, implement and lock down CORS policy in the ASP.NET Core backend to allow cross-origin requests only from the Netlify-hosted frontend domain, satisfying AC-3 and AC-4 of US_005.

This task depends on `task_001_infra_netlify_frontend_deploy.md` and `task_002_infra_railway_backend_deploy.md` for the hosting environment to exist. It does not create new deployment infrastructure but rather hardens the configuration layer on top of it.

## Dependent Tasks

- `task_001_infra_netlify_frontend_deploy.md` — Netlify site must exist; `NETLIFY_SITE_ID` and frontend domain must be known
- `task_002_infra_railway_backend_deploy.md` — Railway service must exist; `RAILWAY_TOKEN` registered; container `Program.cs` entry point must be in place
- US_002 — .net 10 solution must have `appsettings.json` present for IConfiguration base structure

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Api/Program.cs` | MODIFY | Add CORS policy (`WithOrigins` from env var), add startup env validation guard |
| `server/src/PropelIQ.Api/appsettings.json` | MODIFY | Remove any hardcoded connection strings or secrets; replace with empty/placeholder values |
| `server/src/PropelIQ.Api/appsettings.Development.json` | VERIFY | Must be gitignored; never committed with real secrets |
| `.gitignore` | MODIFY | Ensure `appsettings.Development.json` and `**/.env` are excluded |
| `app/src/environments/environment.ts` | MODIFY | Replace hardcoded `apiUrl` with value read from `window.__env.apiUrl` (runtime injection) |
| `app/src/assets/env.js` | CREATE | Runtime environment file injected by Netlify build env vars; loaded via `index.html` `<script>` |
| `app/src/index.html` | MODIFY | Add `<script src="/assets/env.js"></script>` before Angular bootstrap |

## Implementation Plan

1. **Sanitise `appsettings.json` — no hardcoded secrets** — Ensure `appsettings.json` contains no real credentials. Replace any connection strings with empty string `""` or `null`. Comments must document which environment variable is expected (e.g., `// Set via env var: DATABASE_URL`). All secret-bearing keys must have `null` or `""` as their JSON value — never a real value.

2. **Add startup environment validation guard in `Program.cs`** — Before `app.Build()`, validate all required environment variables are present using `IConfiguration`. Throw `InvalidOperationException` with a descriptive message if any required key is absent. Required keys: `DATABASE_URL` (or `ConnectionStrings__DefaultConnection`), `REDIS_URL`, `JWT__SecretKey`, `CORS__AllowedOrigins`. This prevents silent runtime failures with missing config (OWASP A05).

3. **Configure CORS policy in `Program.cs`** — Read `CORS__AllowedOrigins` from `IConfiguration` (set via Railway env var). Call `builder.Services.AddCors(...)` with a named policy `"NetlifyPolicy"`. Apply `WithOrigins(allowedOrigins)` — **no wildcard**. Allow methods `GET, POST, PUT, PATCH, DELETE, OPTIONS`. Allow `Authorization` and `Content-Type` headers. Apply `app.UseCors("NetlifyPolicy")` before `app.UseAuthentication()` and `app.UseAuthorization()`.

4. **Register environment variables in Railway Dashboard** — In Railway service → Variables, add: `DATABASE_URL` (Neon PostgreSQL connection string), `REDIS_URL` (Upstash Redis connection string), `JWT__SecretKey` (minimum 256-bit random string), `JWT__Issuer`, `JWT__Audience`, `CORS__AllowedOrigins` (set to Netlify site URL e.g. `https://propeliq.netlify.app`). Document the full variable list in `docs/env-variables.md` (non-secret names only; values must never be committed).

5. **Configure Angular runtime environment injection** — Create `app/src/assets/env.js`: a plain JS file that assigns `window.__env = { apiUrl: '%%API_URL%%' }`. The Netlify build script substitutes `%%API_URL%%` with the `API_URL` Netlify build environment variable at deploy time using `sed` in the Netlify build command. This avoids baking the API URL into the Angular bundle (which changes across environments).

6. **Update `app/src/environments/environment.ts`** — Replace the hardcoded `apiUrl` string with `(window as any).__env?.apiUrl ?? ''`. The empty fallback deliberately causes a visible failure (API calls will fail with 400/CORS error) if the env file is not loaded — no silent hardcoded fallback per AC-3.

7. **Load `env.js` in `app/src/index.html`** — Add `<script src="/assets/env.js"></script>` as the first `<script>` tag in `<head>` before the Angular bootstrap scripts. This ensures `window.__env` is populated before Angular initialises and reads `environment.ts`.

8. **Update `.gitignore` and verify secrets hygiene** — Confirm `appsettings.Development.json`, `**/.env`, `**/env.local.js` are in `.gitignore`. Run `git grep -r "password\|secret\|connectionstring" -- "*.json" "*.ts" "*.js"` (case-insensitive) to verify no secrets are in tracked files.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── app/                         # Angular 18 workspace
│   ├── angular.json
│   ├── src/
│   │   ├── index.html           # To be modified (add env.js script)
│   │   ├── assets/
│   │   │   └── env.js           # To be created (runtime env injection)
│   │   └── environments/
│   │       └── environment.ts   # To be modified (read from window.__env)
├── server/                      # .net 10 solution
│   └── src/
│       └── PropelIQ.Api/
│           ├── Program.cs       # To be modified (CORS, env validation)
│           ├── appsettings.json # To be sanitised (no hardcoded secrets)
│           └── appsettings.Development.json  # Must be gitignored
├── netlify.toml                 # From us_005 task_001
├── railway.toml                 # From us_005 task_002
└── .gitignore                   # To be verified/updated
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `server/src/PropelIQ.Api/Program.cs` | Add CORS policy with env-driven origins; add startup env validation guard |
| MODIFY | `server/src/PropelIQ.Api/appsettings.json` | Remove all hardcoded secrets; replace with `null`/`""` placeholders |
| MODIFY | `.gitignore` | Add `appsettings.Development.json`, `**/env.local.js`, `**/.env` entries |
| MODIFY | `app/src/environments/environment.ts` | Replace hardcoded `apiUrl` with `(window as any).__env?.apiUrl ?? ''` |
| CREATE | `app/src/assets/env.js` | Runtime env file — `window.__env = { apiUrl: '%%API_URL%%' }` |
| MODIFY | `app/src/index.html` | Add `<script src="/assets/env.js"></script>` in `<head>` before bootstrap |

### Reference: CORS policy in `Program.cs`

```csharp
var allowedOrigins = builder.Configuration["CORS__AllowedOrigins"]
    ?? throw new InvalidOperationException(
        "CORS__AllowedOrigins environment variable is required. " +
        "Set to the Netlify frontend URL (e.g., https://propeliq.netlify.app).");

builder.Services.AddCors(options =>
{
    options.AddPolicy("NetlifyPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type")
              .AllowCredentials();
    });
});

// Apply CORS before auth middleware
app.UseCors("NetlifyPolicy");
app.UseAuthentication();
app.UseAuthorization();
```

### Reference: Startup env validation guard in `Program.cs`

```csharp
// Fail fast on startup if required env vars are missing (OWASP A05)
static void RequireEnvVar(IConfiguration config, string key)
{
    if (string.IsNullOrWhiteSpace(config[key]))
        throw new InvalidOperationException(
            $"Required environment variable '{key}' is not configured.");
}

RequireEnvVar(builder.Configuration, "DATABASE_URL");
RequireEnvVar(builder.Configuration, "REDIS_URL");
RequireEnvVar(builder.Configuration, "JWT__SecretKey");
RequireEnvVar(builder.Configuration, "CORS__AllowedOrigins");
```

### Reference: `app/src/assets/env.js`

```javascript
// Runtime environment injection — substituted at Netlify build time.
// %%API_URL%% is replaced by the Netlify build command using sed.
// DO NOT hardcode values here. This file is committed with placeholder tokens only.
window.__env = {
  apiUrl: '%%API_URL%%'
};
```

### Reference: Updated `netlify.toml` build command (add to task_001 output)

```toml
[build]
  base    = "app"
  command = "npm run build:prod && sed -i 's|%%API_URL%%|'\"$API_URL\"'|g' dist/propeliq/browser/assets/env.js"
  publish = "dist/propeliq/browser"
```

### Required Railway Environment Variables

| Variable Name | Purpose | Example Value |
| ------------- | ------- | ------------- |
| `DATABASE_URL` | Neon PostgreSQL connection string | `Host=...;Database=propeliq;Username=...;Password=...` |
| `REDIS_URL` | Upstash Redis connection string | `rediss://:token@host:port` |
| `JWT__SecretKey` | JWT signing key (min 256-bit) | `<32+ char random secret>` |
| `JWT__Issuer` | JWT issuer claim | `https://propeliq.railway.app` |
| `JWT__Audience` | JWT audience claim | `https://propeliq.netlify.app` |
| `CORS__AllowedOrigins` | Netlify frontend domain (exact, no wildcard) | `https://propeliq.netlify.app` |

### Required Netlify Build Environment Variables

| Variable Name | Purpose | Example Value |
| ------------- | ------- | ------------- |
| `API_URL` | Railway API base URL injected into `env.js` at build time | `https://propeliq.up.railway.app` |

> **Security**: Never commit actual values. Register all secrets via Railway Dashboard → Variables and Netlify Dashboard → Environment Variables. Document only key names in this file.

## External References

- [ASP.NET Core — CORS middleware](https://learn.microsoft.com/en-us/aspnet/core/security/cors)
- [ASP.NET Core — Configuration in .NET (`IConfiguration`)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [ASP.NET Core — Environment-based configuration (`appsettings.*.json`)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments)
- [Railway — Service environment variables](https://docs.railway.app/guides/variables)
- [Netlify — Build environment variables](https://docs.netlify.com/configure-builds/environment-variables/)
- [OWASP — Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- [OWASP — A05:2021 Security Misconfiguration](https://owasp.org/Top10/A05_2021-Security_Misconfiguration/)
- [OWASP — A02:2021 Cryptographic Failures (avoid hardcoded credentials)](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [MDN — CORS: Cross-Origin Resource Sharing](https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/CORS)
- [Angular — Runtime environment configuration patterns](https://angular.dev/guide/build)

## Build Commands

```bash
# .NET — verify no hardcoded secrets in tracked JSON files
git grep -rni "password\|secret\|connectionstring" -- "*.json"
# Expected: no matches (or only comments/placeholders)

# .NET — build and verify startup validation fires on missing env vars
cd server
DATABASE_URL="" dotnet run --project src/PropelIQ.Api
# Expected: InvalidOperationException with message about DATABASE_URL

# .NET — run with all env vars set (local smoke test)
DATABASE_URL="..." REDIS_URL="..." JWT__SecretKey="..." CORS__AllowedOrigins="http://localhost:4200" \
  dotnet run --project server/src/PropelIQ.Api

# Angular — verify env.js substitution locally
cd app && sed "s|%%API_URL%%|http://localhost:5000|g" src/assets/env.js > /tmp/env-test.js && cat /tmp/env-test.js
# Expected: window.__env = { apiUrl: 'http://localhost:5000' }

# CORS smoke test — verify preflight from Netlify domain (after Railway deploy)
curl -X OPTIONS https://<railway-domain>/api/appointments \
  -H "Origin: https://propeliq.netlify.app" \
  -H "Access-Control-Request-Method: GET" \
  -H "Access-Control-Request-Headers: Authorization" \
  -I
# Expected: Access-Control-Allow-Origin: https://propeliq.netlify.app
```

## Implementation Validation Strategy

- [ ] `.NET` startup throws `InvalidOperationException` when any required env var is missing (tested locally)
- [ ] No real secrets appear in any tracked file (`git grep` returns no matches)
- [ ] `appsettings.Development.json` is listed in `.gitignore` and absent from `git status`
- [ ] CORS preflight (`OPTIONS`) from Netlify domain returns `Access-Control-Allow-Origin: https://propeliq.netlify.app`
- [ ] CORS preflight from a foreign origin (e.g., `https://evil.example.com`) is rejected (no `Access-Control-Allow-Origin` header returned)
- [ ] Angular app running on Netlify successfully calls Railway API (end-to-end, no CORS error in browser console)
- [ ] `window.__env.apiUrl` is set to the Railway domain in the deployed Netlify build (verified via browser console)

## Implementation Checklist

- [ ] Sanitise `server/src/PropelIQ.Api/appsettings.json` — remove all hardcoded secrets; replace with `null` or `""`
- [ ] Add startup validation guard to `Program.cs` — `RequireEnvVar` calls for `DATABASE_URL`, `REDIS_URL`, `JWT__SecretKey`, `CORS__AllowedOrigins`
- [ ] Add CORS policy `"NetlifyPolicy"` to `Program.cs` — `WithOrigins` from `CORS__AllowedOrigins` env var; no wildcard; `app.UseCors("NetlifyPolicy")` before auth middleware
- [ ] Create `app/src/assets/env.js` with `window.__env = { apiUrl: '%%API_URL%%' }` placeholder
- [ ] Update `app/src/index.html` — add `<script src="/assets/env.js"></script>` as first script in `<head>`
- [ ] Update `app/src/environments/environment.ts` — replace hardcoded `apiUrl` with `(window as any).__env?.apiUrl ?? ''`
- [ ] Update `netlify.toml` build command to inject `$API_URL` into `env.js` via `sed` substitution
- [ ] Register Railway env vars: `DATABASE_URL`, `REDIS_URL`, `JWT__SecretKey`, `JWT__Issuer`, `JWT__Audience`, `CORS__AllowedOrigins`; register Netlify env var: `API_URL`
