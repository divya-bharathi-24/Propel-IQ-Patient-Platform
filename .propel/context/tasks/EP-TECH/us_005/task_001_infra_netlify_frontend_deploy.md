# Task - TASK_001

## Requirement Reference

- User Story: [us_005] (extracted from input)
- Story Location: [.propel/context/tasks/EP-TECH/us_005/us_005.md]
- Acceptance Criteria:
  - **AC-1**: Given the Netlify site is configured, When a build artifact is pushed, Then the Angular SPA is served via global CDN with a valid HTTPS certificate and LCP < 2.5s for the shell page.
- Edge Case:
  - What happens if Railway free-tier limits are reached? — Not in scope for this task (Railway is task_002). Netlify free tier is separate.
  - How are CORS misconfigurations caught? — Handled in task_003; this task focuses on Netlify-only configuration.

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

> This task configures hosting infrastructure (not UI components), so Design References are N/A.

## Applicable Technology Stack

| Layer          | Technology         | Version |
| -------------- | ------------------ | ------- |
| Frontend       | Angular            | 18.x    |
| Hosting (FE)   | Netlify            | —       |
| CI/CD          | GitHub Actions     | —       |
| Container      | N/A                | N/A     |
| AI/ML          | N/A                | N/A     |
| Vector Store   | N/A                | N/A     |
| AI Gateway     | N/A                | N/A     |
| Mobile         | N/A                | N/A     |

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

Configure the Netlify hosting site for the Angular 18 SPA so that every CD deployment (from US_004 task_002) is served via Netlify's global CDN over HTTPS with LCP < 2.5s on the shell page. This includes creating `netlify.toml` with build settings, SPA redirect rules, security response headers, and performance-oriented caching headers.

This task covers only the **Netlify frontend hosting** configuration. Railway backend deployment is handled in `task_002_infra_railway_backend_deploy.md`. Environment variable management is handled in `task_003_infra_env_vars_cors_policy.md`.

## Dependent Tasks

- US_001 — Angular workspace must exist (`ng build --configuration production` must succeed, `outputPath` known)
- US_004 task_002 — CD pipeline must call `nwtgck/actions-netlify` or `amondnet/vercel-action`; the Netlify site must exist before the deploy token is registered as a secret

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `netlify.toml` (repository root) | CREATE | Netlify build, redirect, and header configuration |
| `app/angular.json` | VERIFY | `outputPath` must match `netlify.toml` `publish` dir |
| `app/src/index.html` | MODIFY | Add `<link rel="preconnect">` hints for API domain to aid LCP |

## Implementation Plan

1. **Create Netlify site via Netlify UI** — Log in to Netlify, create a new site (manual deploy or linked to GitHub repo). Record `Site ID` and generate a `Personal Access Token`. Register both as GitHub secrets `NETLIFY_SITE_ID` and `NETLIFY_AUTH_TOKEN` (used by CD pipeline from task_002 of US_004).

2. **Create `netlify.toml` — build settings** — Set `[build]` section: `base = "app"`, `command = "npm run build:prod"` (maps to `ng build --configuration production`), `publish = "dist/propeliq/browser"` (match Angular `outputPath`). Add `[build.environment]` with `NODE_VERSION = "20"`.

3. **Configure SPA redirect rule** — Add `[[redirects]]` entry: `from = "/*"`, `to = "/index.html"`, `status = 200`. This prevents 404 on browser refresh of Angular client-side routes.

4. **Enforce HTTPS-only** — Set `force = true` on a redirect from `http` → `https` so all HTTP traffic is permanently redirected (status 301). Netlify automatically provisions a Let's Encrypt certificate for the custom/default domain.

5. **Add security response headers** — Add `[[headers]]` for path `"/*"` with:
   - `X-Frame-Options: DENY`
   - `X-Content-Type-Options: nosniff`
   - `Referrer-Policy: strict-origin-when-cross-origin`
   - `Permissions-Policy: camera=(), microphone=(), geolocation=()`
   - `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`

6. **Add asset caching headers** — Add `[[headers]]` for path `"/assets/*"` and `"/*.js"`, `"/*.css"` with `Cache-Control: public, max-age=31536000, immutable` (Angular hashed file names make this safe).

7. **Add LCP preconnect hints to `index.html`** — Add `<link rel="preconnect" href="<RAILWAY_API_URL>" crossorigin>` and `<link rel="dns-prefetch" href="<RAILWAY_API_URL>">` in `<head>` to reduce connection setup latency for API calls (use placeholder replaced by runtime env config).

8. **Verify LCP < 2.5s** — After deploy, use Netlify's built-in Lighthouse CI integration or run Lighthouse via GitHub Actions against the Netlify preview URL. Confirm LCP < 2.5s for the shell page (`/`) per NFR-012.

## Current Project State

```
Propel-IQ-Patient-Platform/
├── app/                         # Angular 18 workspace (from US_001)
│   ├── angular.json
│   ├── package.json
│   └── src/
│       └── index.html
├── server/                      # .net 10 solution (from US_002)
├── .github/
│   └── workflows/
│       ├── ci.yml               # From US_004 task_001
│       └── cd.yml               # From US_004 task_002
└── netlify.toml                 # To be created
```

_Update this tree during execution based on completed dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `netlify.toml` | Netlify build, SPA redirect, HTTPS enforcement, security and caching headers |
| MODIFY | `app/src/index.html` | Add `<link rel="preconnect">` and `<link rel="dns-prefetch">` for API domain |
| VERIFY | `app/angular.json` | Confirm `outputPath` under `projects.<name>.architect.build.options` matches `netlify.toml` `publish` dir |

### Reference: `netlify.toml` Structure

```toml
[build]
  base    = "app"
  command = "npm run build:prod"
  publish = "dist/propeliq/browser"

[build.environment]
  NODE_VERSION = "20"

# SPA fallback routing
[[redirects]]
  from   = "/*"
  to     = "/index.html"
  status = 200

# HTTPS enforcement
[[redirects]]
  from  = "http://propeliq.netlify.app/*"
  to    = "https://propeliq.netlify.app/:splat"
  status = 301
  force  = true

# Security headers
[[headers]]
  for = "/*"
  [headers.values]
    X-Frame-Options              = "DENY"
    X-Content-Type-Options       = "nosniff"
    Referrer-Policy              = "strict-origin-when-cross-origin"
    Permissions-Policy           = "camera=(), microphone=(), geolocation=()"
    Strict-Transport-Security    = "max-age=31536000; includeSubDomains; preload"

# Long-lived caching for hashed static assets
[[headers]]
  for = "/assets/*"
  [headers.values]
    Cache-Control = "public, max-age=31536000, immutable"

[[headers]]
  for = "/*.js"
  [headers.values]
    Cache-Control = "public, max-age=31536000, immutable"

[[headers]]
  for = "/*.css"
  [headers.values]
    Cache-Control = "public, max-age=31536000, immutable"
```

## External References

- [Netlify — File-based configuration (`netlify.toml`)](https://docs.netlify.com/configure-builds/file-based-configuration/)
- [Netlify — Redirects and rewrites (SPA fallback)](https://docs.netlify.com/routing/redirects/)
- [Netlify — Custom headers](https://docs.netlify.com/routing/headers/)
- [Netlify — HTTPS and TLS (automatic Let's Encrypt)](https://docs.netlify.com/domains-https/https-ssl/)
- [Netlify — Deploy with GitHub Actions (`nwtgck/actions-netlify`)](https://github.com/nwtgck/actions-netlify)
- [Angular — Build configuration and `outputPath`](https://angular.dev/reference/configs/workspace-config)
- [web.dev — Optimize LCP (Largest Contentful Paint)](https://web.dev/articles/optimize-lcp)
- [OWASP — HTTP Security Response Headers Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)
- [MDN — `rel=preconnect` resource hint](https://developer.mozilla.org/en-US/docs/Web/HTML/Attributes/rel/preconnect)

## Build Commands

```bash
# Angular — confirm production build output path
cd app && npm ci && npx ng build --configuration production
# Output should appear in dist/propeliq/browser/ (adjust name if different)

# Netlify CLI — local preview (requires Netlify CLI installed)
npm install -g netlify-cli
netlify dev

# Netlify CLI — manual deploy to Netlify (for smoke testing)
netlify deploy --dir=app/dist/propeliq/browser --site=$NETLIFY_SITE_ID --auth=$NETLIFY_AUTH_TOKEN
netlify deploy --prod --dir=app/dist/propeliq/browser --site=$NETLIFY_SITE_ID --auth=$NETLIFY_AUTH_TOKEN

# Lighthouse CI — run LCP check against deployed URL
npm install -g @lhci/cli
lhci autorun --upload.target=temporary-public-storage
```

## Implementation Validation Strategy

- [ ] `netlify.toml` committed to repository root and Netlify build picks it up on next deploy
- [ ] Angular SPA loads at Netlify URL without 404 on any client-side route (e.g., `/appointments`)
- [ ] Browser address bar shows `https://` with valid certificate (no mixed-content warnings)
- [ ] HTTP → HTTPS redirect works: `curl -I http://<netlify-url>/` returns `301`
- [ ] Security headers present in response: `curl -I https://<netlify-url>/` shows `X-Frame-Options: DENY`
- [ ] Lighthouse LCP score < 2.5s on `/` (run via Lighthouse CI or Chrome DevTools on deployed URL)
- [ ] Hashed JS/CSS assets served with `Cache-Control: immutable`

## Implementation Checklist

- [ ] Create Netlify site via Netlify UI; record Site ID; generate Personal Access Token
- [ ] Register `NETLIFY_SITE_ID` and `NETLIFY_AUTH_TOKEN` as GitHub repository secrets
- [ ] Create `netlify.toml` at repository root with `[build]`, `[[redirects]]` (SPA + HTTPS), `[[headers]]` (security + caching)
- [ ] Verify `app/angular.json` `outputPath` matches `netlify.toml` `publish` value
- [ ] Add `<link rel="preconnect">` and `<link rel="dns-prefetch">` for API domain to `app/src/index.html`
- [ ] Trigger a CD deploy (push to `main`); confirm Netlify deploy log shows no errors
- [ ] Validate HTTPS certificate and security response headers using `curl -I` or browser DevTools
- [ ] Run Lighthouse against deployed URL; confirm LCP < 2.5s on shell page
