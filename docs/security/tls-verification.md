# TLS Verification Evidence

**Document:** `docs/security/tls-verification.md`  
**Standard:** NFR-005, AG-2, AC-4 — All client-server connections must negotiate TLS 1.2 or higher; TLS 1.0 and TLS 1.1 connections must be actively rejected.  
**Compliance:** OWASP Transport Layer Security Cheat Sheet · HIPAA Security Rule §164.312(e)(2)(ii)

---

## 1. Platform TLS Scope

| Tier                | Host      | TLS Termination                     | Minimum Protocol                       |
| ------------------- | --------- | ----------------------------------- | -------------------------------------- |
| Backend API         | Railway   | Platform ingress (not Kestrel)      | TLS 1.2 (platform default)             |
| Frontend SPA        | Netlify   | Netlify CDN                         | TLS 1.2 (platform default)             |
| PostgreSQL          | Neon      | Database driver (`sslmode=require`) | TLS 1.2                                |
| Redis cache         | Upstash   | `rediss://` scheme (TLS enforced)   | TLS 1.2                                |
| Local dev (Kestrel) | localhost | Kestrel HTTPS                       | TLS 1.2 / 1.3 (ConfigureHttpsDefaults) |

---

## 2. Verification Commands

### 2.1 Backend API — Railway Deployment

Run the following against the Railway-assigned domain after each deployment.

#### Verify TLS 1.1 is rejected

```bash
openssl s_client -connect <your-app>.railway.app:443 -tls1_1 </dev/null 2>&1
```

**Expected output (pass):**

```
CONNECTED(00000003)
...
handshake failure
```

or

```
write:errno=104
```

> If `CONNECTED` appears followed by cipher negotiation, TLS 1.1 is being **accepted** — this is a **failure**.

#### Verify TLS 1.2 is accepted

```bash
openssl s_client -connect <your-app>.railway.app:443 -tls1_2 </dev/null 2>&1 | grep -E "CONNECTED|Protocol"
```

**Expected output (pass):**

```
CONNECTED(00000003)
...
Protocol  : TLSv1.2
```

#### Verify TLS 1.3 is accepted

```bash
openssl s_client -connect <your-app>.railway.app:443 -tls1_3 </dev/null 2>&1 | grep -E "CONNECTED|Protocol"
```

**Expected output (pass):**

```
CONNECTED(00000003)
...
Protocol  : TLSv1.3
```

#### Verify HSTS header

```bash
curl -sI https://<your-app>.railway.app/health | grep -i strict-transport-security
```

**Expected output (pass):**

```
strict-transport-security: max-age=63072000; includeSubDomains; preload
```

---

### 2.2 Local Kestrel — Development

Run after `dotnet dev-certs https --trust` and `dotnet run`.

```bash
# Verify TLS 1.1 is rejected by Kestrel
openssl s_client -connect localhost:5001 -tls1_1 </dev/null 2>&1 | grep -E "CONNECTED|handshake failure|error"

# Verify TLS 1.2 is accepted
openssl s_client -connect localhost:5001 -tls1_2 </dev/null 2>&1 | grep -E "CONNECTED|Protocol"

# Verify HTTP redirects to HTTPS
curl -I http://localhost:5000/health
# Expected: HTTP/1.1 301 Moved Permanently  →  Location: https://localhost:5001/health

# Verify HSTS header on HTTPS response
curl -skI https://localhost:5001/health | grep -i strict-transport-security
# Expected: strict-transport-security: max-age=63072000; includeSubDomains; preload
```

---

### 2.3 Netlify Frontend

```bash
# Verify HSTS header on the Netlify-deployed SPA
curl -sI https://<your-site>.netlify.app/ | grep -i strict-transport-security
# Expected: strict-transport-security: max-age=63072000; includeSubDomains; preload

# Verify X-Content-Type-Options
curl -sI https://<your-site>.netlify.app/ | grep -i x-content-type-options
# Expected: x-content-type-options: nosniff

# Verify X-Frame-Options
curl -sI https://<your-site>.netlify.app/ | grep -i x-frame-options
# Expected: x-frame-options: DENY
```

---

### 2.4 Neon PostgreSQL — SSL Connection Verification

Connection string format (`DATABASE_URL` environment variable — **never commit credentials**):

```
Host=<neon-host>.neon.tech;Database=<db>;Username=<user>;Password=<pass>;SSL Mode=Require;Trust Server Certificate=false
```

Verify SSL is enforced:

```bash
# psql: connect and check ssl status
psql "$DATABASE_URL" -c "SELECT ssl, version FROM pg_stat_ssl WHERE pid = pg_backend_pid();"
# Expected: ssl = t  (true)
```

If the connection string omits `SSL Mode=Require`, the Npgsql driver defaults to `Prefer` (opportunistic TLS).  
Setting `Trust Server Certificate=false` ensures the server certificate is validated against system CAs.

---

### 2.5 Upstash Redis — TLS Scheme Verification

The `REDIS_URL` environment variable must use the `rediss://` scheme (double-s = TLS).

```bash
echo "$REDIS_URL" | grep -c "^rediss://"
# Expected: 1
```

Upstash enforces TLS 1.2+ on all `rediss://` connections at the platform level.

---

## 3. CI Automation

The GitHub Actions workflow (`.github/workflows/ci.yml`) **Gate 5 — TLS Enforcement Smoke Tests** runs automatically on every pull request after all build/test gates pass. It performs steps 2.1 checks against the Railway domain stored in the `API_DOMAIN` repository secret.

To run the CI smoke-test locally against a deployed instance:

```bash
export API_DOMAIN=<your-app>.railway.app

# TLS 1.1 must be rejected
result=$(openssl s_client -connect "$API_DOMAIN:443" -tls1_1 </dev/null 2>&1 || true)
if echo "$result" | grep -q "CONNECTED"; then echo "FAIL"; else echo "PASS"; fi

# TLS 1.2 must be accepted
result=$(openssl s_client -connect "$API_DOMAIN:443" -tls1_2 </dev/null 2>&1 || true)
if echo "$result" | grep -q "CONNECTED"; then echo "PASS"; else echo "FAIL"; fi
```

---

## 4. Re-running Verification After Infrastructure Changes

Re-run these checks whenever any of the following change:

- Railway project settings or domain configuration
- Netlify site configuration or `_headers` file
- Neon PostgreSQL connection string or SSL configuration
- Upstash Redis connection string scheme
- `Program.cs` Kestrel or middleware configuration
- .NET SDK or ASP.NET Core runtime upgrade

---

## 5. References

- [OWASP TLS Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html)
- [ASP.NET Core HSTS Middleware](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl#http-strict-transport-security-protocol-hsts)
- [ASP.NET Core Kestrel ConfigureHttpsDefaults](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints#configure-https-defaults)
- [Netlify Custom Headers](https://docs.netlify.com/routing/headers/)
- [Neon PostgreSQL SSL](https://neon.tech/docs/connect/connect-from-any-app#ssl-connections)
- [Upstash Redis TLS](https://upstash.com/docs/redis/howto/connectwithupstash)
- [HIPAA §164.312(e)(2)(ii) — Transmission Security](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)
