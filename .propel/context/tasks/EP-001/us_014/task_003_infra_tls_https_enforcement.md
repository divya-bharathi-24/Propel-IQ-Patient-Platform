# Task - task_003_infra_tls_https_enforcement

## Requirement Reference

- **User Story:** us_014 — Rate Limiting, Input Validation & Encryption Controls
- **Story Location:** `.propel/context/tasks/EP-001/us_014/us_014.md`
- **Acceptance Criteria:**
  - AC-4: All client-server connections negotiate only TLS 1.2 or higher; TLS 1.0 and TLS 1.1 connections are actively rejected; verifiable via `openssl s_client -tls1_1` returning a handshake failure
- **Edge Cases:**
  - Rate limiter alert: if >50% of requests return 429 within 1 minute, a Serilog alert fires — this applies equally over HTTPS (covered by TLS enforcement; alert mechanism is in task_001)

---

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

---

## Applicable Technology Stack

| Layer                    | Technology                          | Version |
| ------------------------ | ----------------------------------- | ------- |
| Backend Runtime          | ASP.NET Core (Kestrel)              | .NET 9  |
| Frontend Hosting         | Netlify / Vercel                    | —       |
| Backend Hosting          | Railway (free tier)                 | —       |
| Database Hosting         | Neon PostgreSQL (free tier)         | —       |
| Cache Hosting            | Upstash Redis (serverless)          | —       |
| CI/CD                    | GitHub Actions                      | —       |
| TLS Verification         | OpenSSL CLI                         | 3.x     |
| Testing — Integration    | xUnit + HttpClient                  | 2.x     |
| AI/ML                    | N/A                                 | N/A     |
| Mobile                   | N/A                                 | N/A     |

> All code and libraries MUST be compatible with versions above.

---

## AI References (AI Tasks Only)

| Reference Type        | Value |
| --------------------- | ----- |
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

---

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
| ------------------- | ----- |
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

---

## Task Overview

Enforce TLS 1.2+ across all four tiers of the platform's hosting stack (NFR-005, AG-2):

1. **Kestrel / ASP.NET Core** — configure `KestrelServerOptions` to set `SslProtocols.Tls12 | SslProtocols.Tls13` and reject lower protocol versions; add `UseHttpsRedirection()` middleware so all HTTP requests receive HTTP 301 to HTTPS.

2. **Railway (backend hosting)** — verify Railway's TLS termination layer enforces TLS 1.2+ by default; document the verification command and add it to the CI smoke-test.

3. **Netlify / Vercel (frontend hosting)** — enable HSTS in the `_headers` file (frontend) and verify the CDN enforces TLS 1.2+ minimum; configure `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`.

4. **Neon PostgreSQL (database)** — verify `sslmode=require` on the connection string and confirm Neon enforces TLS 1.2+ on all PostgreSQL connections; document the connection string format.

The task also adds a GitHub Actions CI step that runs `openssl s_client` against the deployed API and fails the pipeline if TLS 1.1 or lower is accepted.

---

## Dependent Tasks

- **US_002** — API gateway middleware pipeline must exist before HTTPS redirect middleware can be inserted
- **US_011** (EP-001) — Backend must be deployable to Railway before TLS verification can run
- **task_001_be_rate_limiting_validation_pipeline** (EP-001/us_014) — `Program.cs` is already being modified; TLS middleware must be registered in the same file in correct order

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| MODIFY | `Server/Program.cs` | Add `UseHttpsRedirection()`; configure Kestrel `SslProtocols` |
| MODIFY | `Server/appsettings.json` | Add Kestrel HTTPS endpoint configuration section |
| MODIFY | `Server/appsettings.Production.json` | Production HTTPS certificate path / Railway TLS env vars |
| CREATE | `frontend/public/_headers` | Netlify HSTS header configuration |
| MODIFY | `.github/workflows/ci.yml` | Add `openssl s_client` TLS version smoke-test step |
| CREATE | `docs/security/tls-verification.md` | Evidence document: OpenSSL verification commands and expected output |

---

## Implementation Plan

1. **Kestrel TLS protocol restriction** in `Program.cs`:

   ```csharp
   builder.WebHost.ConfigureKestrel(options =>
   {
       options.ConfigureHttpsDefaults(httpsOptions =>
       {
           httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
           // TLS 1.0 and TLS 1.1 are omitted → Kestrel rejects those handshakes
       });
   });
   ```

   > Note: On Railway, TLS is terminated at the platform's ingress (not by Kestrel). The `ConfigureHttpsDefaults` setting applies for local development and any direct TLS scenarios. For Railway production, step 3 covers verification.

2. **HTTPS redirect middleware** in `Program.cs` (must precede `UseRouting`):

   ```csharp
   app.UseHttpsRedirection();    // HTTP → HTTPS 301 redirect
   app.UseHsts();                // Sends HSTS header on every HTTPS response
   ```

   Configure HSTS options:
   ```csharp
   builder.Services.AddHsts(options =>
   {
       options.MaxAge = TimeSpan.FromDays(730);     // 2 years
       options.IncludeSubDomains = true;
       options.Preload = true;
   });
   ```

3. **Railway TLS verification**:
   - Railway enforces TLS 1.2+ at its load balancer by default (documented in Railway docs as of 2024)
   - Verification command (to be run during deploy smoke-test):
     ```bash
     openssl s_client -connect ${RAILWAY_DOMAIN}:443 -tls1_1 2>&1 | grep -E "CONNECTED|handshake failure"
     # Expected: "handshake failure" (TLS 1.1 rejected)
     openssl s_client -connect ${RAILWAY_DOMAIN}:443 -tls1_2 2>&1 | grep "CONNECTED"
     # Expected: "CONNECTED" (TLS 1.2 accepted)
     ```

4. **Netlify HSTS via `_headers`** file at `frontend/public/_headers`:

   ```
   /*
     Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
     X-Content-Type-Options: nosniff
     X-Frame-Options: DENY
     Referrer-Policy: strict-origin-when-cross-origin
     Permissions-Policy: geolocation=(), microphone=()
   ```

   > Netlify automatically terminates TLS 1.0/1.1; this step enforces HSTS to prevent protocol downgrade attacks.

5. **Neon PostgreSQL `sslmode=require`** in connection string:

   ```
   Host=<neon-host>;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Require;Trust Server Certificate=false
   ```

   - Store connection string in environment variable `DATABASE_CONNECTION_STRING` (never committed to git)
   - Add to `appsettings.Production.json` template as `"ConnectionStrings": { "Default": "" }` with empty value and environment-variable override instruction in README

6. **GitHub Actions CI smoke-test** step (`.github/workflows/ci.yml`):

   ```yaml
   - name: Verify TLS 1.1 rejected
     run: |
       result=$(openssl s_client -connect ${{ secrets.API_DOMAIN }}:443 -tls1_1 2>&1)
       if echo "$result" | grep -q "CONNECTED"; then
         echo "ERROR: TLS 1.1 accepted — FAIL"
         exit 1
       fi
       echo "TLS 1.1 rejected as expected — PASS"

   - name: Verify TLS 1.2 accepted
     run: |
       openssl s_client -connect ${{ secrets.API_DOMAIN }}:443 -tls1_2 < /dev/null \
         | grep "CONNECTED" || (echo "ERROR: TLS 1.2 not accepted" && exit 1)
   ```

7. **Evidence documentation** at `docs/security/tls-verification.md`:
   - Record expected output of each verification command
   - Include instructions for re-running verification after infrastructure changes
   - Note that Upstash Redis TLS is enforced by the platform (`rediss://` scheme)

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
│   └── workflows/    (ci.yml to be modified)
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update this section with actual tree after project scaffold and Railway/Netlify deployment is configured.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| MODIFY | `Server/Program.cs` | Configure Kestrel `SslProtocols.Tls12 \| Tls13`; add `UseHttpsRedirection()` and `UseHsts()` |
| MODIFY | `Server/appsettings.json` | Add `Kestrel:Endpoints:Https` section; reference cert from env var |
| MODIFY | `Server/appsettings.Production.json` | Production TLS/cert configuration for Railway; empty `DATABASE_CONNECTION_STRING` placeholder |
| CREATE | `frontend/public/_headers` | Netlify security headers: HSTS, X-Content-Type-Options, X-Frame-Options |
| MODIFY | `.github/workflows/ci.yml` | Add OpenSSL TLS 1.1/1.2 smoke-test steps after deployment |
| CREATE | `docs/security/tls-verification.md` | TLS verification evidence: commands, expected output, re-run instructions |

---

## External References

- [ASP.NET Core Kestrel — ConfigureHttpsDefaults (SslProtocols)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-9.0#configure-https-defaults)
- [ASP.NET Core HSTS Middleware](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-9.0)
- [ASP.NET Core HTTPS Redirection Middleware](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-9.0#require-https)
- [Railway TLS Termination Documentation](https://docs.railway.app/reference/static-outbound-ips#ssl-tls)
- [Netlify — Custom Headers (_headers file)](https://docs.netlify.com/routing/headers/)
- [Neon PostgreSQL — SSL Connection](https://neon.tech/docs/connect/connect-from-any-app#ssl-connections)
- [Upstash Redis — TLS (rediss:// scheme)](https://upstash.com/docs/redis/howto/connectwithupstash)
- [OWASP Transport Layer Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html)
- [HIPAA Security Rule — Transmission Security (45 CFR §164.312(e)(2)(ii))](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

---

## Build Commands

```bash
# Run backend locally with HTTPS (dev cert)
dotnet dev-certs https --trust
dotnet run --project Server/Server.csproj

# Verify TLS 1.1 rejected (local Kestrel)
openssl s_client -connect localhost:5001 -tls1_1 2>&1 | grep -E "CONNECTED|handshake failure"

# Verify TLS 1.2 accepted (local Kestrel)
openssl s_client -connect localhost:5001 -tls1_2 2>&1 | grep "CONNECTED"

# Run CI smoke-test locally (requires deployed Railway URL in env)
export API_DOMAIN=your-app.railway.app
openssl s_client -connect ${API_DOMAIN}:443 -tls1_1 < /dev/null 2>&1
```

---

## Implementation Validation Strategy

- [ ] `openssl s_client -tls1_1` against local Kestrel returns `handshake failure` (not `CONNECTED`)
- [ ] `openssl s_client -tls1_2` against local Kestrel returns `CONNECTED`
- [ ] HTTP request to the backend returns HTTP 301 redirect to HTTPS (`curl -I http://...`)
- [ ] HSTS header (`Strict-Transport-Security`) is present on all HTTPS API responses
- [ ] Netlify `_headers` file present; deployed frontend includes HSTS header in response
- [ ] PostgreSQL connection string uses `SSL Mode=Require`; connection fails without SSL
- [ ] GitHub Actions CI step fails if TLS 1.1 is accepted on the deployed Railway domain
- [ ] `DATABASE_CONNECTION_STRING` env var is never committed to git (validated by `git grep` in CI)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Configure `KestrelServerOptions.ConfigureHttpsDefaults` with `SslProtocols.Tls12 | SslProtocols.Tls13` in `Program.cs`
- [ ] Add `app.UseHttpsRedirection()` and `app.UseHsts()` with 2-year max-age + includeSubDomains + preload
- [ ] Add `Kestrel:Endpoints:Https` section to `appsettings.json`; reference dev cert via environment variable
- [ ] Create `frontend/public/_headers` with HSTS, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy
- [ ] Set Neon PostgreSQL connection string to use `SSL Mode=Require;Trust Server Certificate=false` stored in env var `DATABASE_CONNECTION_STRING`
- [ ] Add GitHub Actions CI steps: `openssl s_client -tls1_1` must fail; `openssl s_client -tls1_2` must succeed
- [ ] Create `docs/security/tls-verification.md` with verification commands and expected output evidence
- [ ] Confirm Upstash Redis connection uses `rediss://` (TLS scheme) in `REDIS_CONNECTION_STRING` env var
