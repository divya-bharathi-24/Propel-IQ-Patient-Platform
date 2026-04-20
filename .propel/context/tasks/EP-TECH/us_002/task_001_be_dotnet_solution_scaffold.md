# Task - task_001_be_dotnet_solution_scaffold

## Requirement Reference

- User Story: [us_002]
- Story Location: [.propel/context/tasks/EP-TECH/us_002/us_002.md]
- Acceptance Criteria:
  - AC1: Given the .NET 9 solution is scaffolded, When I run `dotnet build`, Then all seven module projects (Auth, Patient, Appointment, Clinical, AI, Notification, Admin) compile successfully with zero errors.
  - AC2: Given the solution structure is created, When I inspect the module boundaries, Then each module has its own Commands, Queries, Handlers (MediatR), and Validators (FluentValidation) folders with no direct cross-module references.
  - AC3: Given the API Gateway is configured, When an HTTP request arrives, Then it passes through correlation ID injection middleware, RBAC middleware placeholder, and rate limiting middleware before routing to the appropriate module handler.
  - AC4: Given the solution is running, When I navigate to `/swagger`, Then the OpenAPI 3.0 Swagger UI loads with all scaffolded endpoints documented and JWT Bearer authentication configured.
- Edge Case:
  - Accidental cross-module dependency: .NET project references are restricted at the solution level; only `Propel.Api.Gateway` references module projects; no module project references another module project. Enforced at compile time.
  - Gateway unavailable downstream module: Returns HTTP 503 with a structured JSON error response containing a correlation ID field (`{ "correlationId": "...", "error": "Service unavailable" }`).

## Design References (Frontend Tasks Only)

| Reference Type        | Value |
|-----------------------|-------|
| **UI Impact**         | No    |
| **Figma URL**         | N/A   |
| **Wireframe Status**  | N/A   |
| **Wireframe Type**    | N/A   |
| **Wireframe Path/URL**| N/A   |
| **Screen Spec**       | N/A   |
| **UXR Requirements**  | N/A   |
| **Design Tokens**     | N/A   |

## Applicable Technology Stack

| Layer              | Technology                  | Version |
|--------------------|-----------------------------|---------|
| Backend            | ASP.NET Core Web API        | .NET 9  |
| Backend Messaging  | MediatR                     | 12.x    |
| Backend Validation | FluentValidation            | 11.x    |
| ORM                | Entity Framework Core       | 9.x     |
| Database           | PostgreSQL (Neon — free tier) | 16+   |
| Cache              | Upstash Redis               | Serverless |
| API Documentation  | Swagger / OpenAPI 3.0       | —       |
| Authentication     | JWT + bcrypt/Argon2         | —       |

**Note**: All code and libraries MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type        | Value |
|-----------------------|-------|
| **AI Impact**         | No    |
| **AIR Requirements**  | N/A   |
| **AI Pattern**        | N/A   |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A   |
| **Model Provider**    | N/A   |

## Mobile References (Mobile Tasks Only)

| Reference Type      | Value |
|---------------------|-------|
| **Mobile Impact**   | No    |
| **Platform Target** | N/A   |
| **Min OS Version**  | N/A   |
| **Mobile Framework**| N/A   |

## Task Overview

Scaffold the .NET 9 ASP.NET Core Web API backend solution for the Unified Patient Access & Clinical Intelligence Platform. The solution is structured as a modular monolith (AG-3) with a single deployable API gateway host (`Propel.Api.Gateway`) and seven isolated module class library projects: Auth, Patient, Appointment, Clinical, AI, Notification, and Admin. Each module encapsulates its own CQRS artefacts (Commands, Queries, Handlers) wired through MediatR 12.x (TR-019) and its own request validators via FluentValidation 11.x (TR-020). The gateway host implements a middleware pipeline providing correlation ID propagation, an RBAC stub, and ASP.NET Core built-in rate limiting before dispatching to module handlers. Swagger/OpenAPI 3.0 with JWT Bearer auth is exposed at `/swagger` (TR-006). Entity Framework Core 9 (TR-003) and PostgreSQL connection string configuration (TR-004) are registered in the host; Upstash Redis (TR-005) connection is configured as a placeholder — actual schema migrations and Redis usage are delivered in subsequent stories. This task enables all backend feature teams to develop within clearly separated domain modules from day one.

## Dependent Tasks

- None — This is a foundational scaffolding story for the backend. No prior tasks required.

## Impacted Components

| Component / Module                          | Action | Notes                                                     |
|---------------------------------------------|--------|-----------------------------------------------------------|
| `server/` (solution root)                   | CREATE | .NET 9 solution folder and `Propel.sln` file              |
| `server/Propel.Api.Gateway/`                | CREATE | ASP.NET Core Web API host; single deployable entry point  |
| `server/Propel.Modules.Auth/`               | CREATE | Auth bounded module class library                         |
| `server/Propel.Modules.Patient/`            | CREATE | Patient bounded module class library                      |
| `server/Propel.Modules.Appointment/`        | CREATE | Appointment bounded module class library                  |
| `server/Propel.Modules.Clinical/`           | CREATE | Clinical bounded module class library                     |
| `server/Propel.Modules.AI/`                 | CREATE | AI bounded module class library                           |
| `server/Propel.Modules.Notification/`       | CREATE | Notification bounded module class library                 |
| `server/Propel.Modules.Admin/`              | CREATE | Admin bounded module class library                        |
| `server/Propel.Api.Gateway/Middleware/CorrelationIdMiddleware.cs` | CREATE | Propagates `X-Correlation-ID` header |
| `server/Propel.Api.Gateway/Middleware/RbacMiddleware.cs`         | CREATE | Stub; reads JWT claims; ready for policy enforcement |
| `server/Propel.Api.Gateway/Program.cs`      | CREATE | Service registration, middleware pipeline, Swagger config |
| `server/Propel.Api.Gateway/appsettings.json`| CREATE | PostgreSQL connection string, Upstash Redis config, JWT settings |

## Implementation Plan

1. **Create .NET 9 solution and project structure** — In the `server/` folder, run `dotnet new sln -n Propel`. Create the API gateway host with `dotnet new webapi -n Propel.Api.Gateway --use-minimal-apis false`. Create each of the seven module class libraries with `dotnet new classlib -n Propel.Modules.<Name>`. Add all projects to the solution. Configure project references: `Propel.Api.Gateway` references each of the seven modules; no module project references another module. This enforces the cross-module isolation boundary (AC2 edge case).

2. **Scaffold MediatR CQRS folder structure per module** — Install `MediatR` 12.x (`dotnet add package MediatR`) in each module project. Under each module's root, create sub-folders: `Commands/`, `Queries/`, `Handlers/`. Add one sample command (`Ping<Module>Command : IRequest<string>`) and its handler (`Ping<Module>CommandHandler : IRequestHandler<Ping<Module>Command, string>`) per module to confirm MediatR wiring. Register `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(...))` in `Program.cs` covering all seven module assemblies. This satisfies AC2.

3. **Scaffold FluentValidation per module** — Install `FluentValidation.AspNetCore` 11.x in each module project. Under each module's root, create a `Validators/` sub-folder. Add one sample validator (`Ping<Module>CommandValidator : AbstractValidator<Ping<Module>Command>`) per module. In `Program.cs`, register FluentValidation as a pipeline behaviour via `services.AddFluentValidationAutoValidation()` and `services.AddValidatorsFromAssemblies(...)` covering all seven module assemblies. This satisfies TR-020 and AC2.

4. **Register EF Core 9, PostgreSQL, and Upstash Redis in the host** — Install `Microsoft.EntityFrameworkCore` 9.x and `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x in `Propel.Api.Gateway`. Create a placeholder `AppDbContext : DbContext` with no `DbSet` properties yet (actual entity configuration is deferred to feature stories). Register via `services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")))`. Add `ConnectionStrings.DefaultConnection` (Neon PostgreSQL URL), `Redis.ConnectionString` (Upstash Redis URL), and `Jwt.Secret` / `Jwt.Issuer` / `Jwt.Audience` keys to `appsettings.json` as placeholder strings. This satisfies TR-003, TR-004, TR-005 (configuration layer) without creating migrations.

5. **Implement CorrelationIdMiddleware** — In `Propel.Api.Gateway/Middleware/CorrelationIdMiddleware.cs`, implement `IMiddleware`. If the incoming request contains `X-Correlation-ID` header, read it; otherwise generate a new `Guid.NewGuid().ToString()`. Store the value in `HttpContext.Items["CorrelationId"]` and append it to the response headers. Register with `app.UseMiddleware<CorrelationIdMiddleware>()` before all other middleware in `Program.cs`. This satisfies AC3.

6. **Implement RBAC middleware placeholder and rate limiting** — Create `RbacMiddleware.cs` as a pass-through stub that reads `HttpContext.User.Claims` and sets a placeholder authorization context (no actual policy enforcement yet; logs claims for observability). Install ASP.NET Core built-in rate limiting (`Microsoft.AspNetCore.RateLimiting` — included in .NET 9). Configure a fixed-window policy in `Program.cs`: `services.AddRateLimiter(opt => opt.AddFixedWindowLimiter("global", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 100; }))`. Apply with `app.UseRateLimiter()`. This satisfies AC3.

7. **Configure Swagger / OpenAPI 3.0 with JWT Bearer** — In `Program.cs`, call `services.AddSwaggerGen(c => { c.SwaggerDoc("v1", new OpenApiInfo { Title = "Propel IQ API", Version = "v1" }); c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { ... }); c.AddSecurityRequirement(...); })`. Add `app.UseSwagger()` and `app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Propel IQ API v1"))` in the HTTP pipeline. Add one stub controller per module (e.g., `AuthController`, `PatientController`) with a single `[HttpGet("ping")]` action returning `Ok()` to confirm endpoints appear in Swagger UI. This satisfies AC4.

8. **Validate all ACs** — Run `dotnet build` from the `server/` root and confirm zero errors (AC1). Inspect each module's folder structure for Commands, Queries, Handlers, Validators sub-folders (AC2). Run `dotnet run --project Propel.Api.Gateway` and send a test request to `/api/auth/ping` — verify `X-Correlation-ID` appears in the response headers (AC3). Navigate to `https://localhost:<port>/swagger` and confirm all seven module controllers appear with the JWT Bearer auth badge (AC4).

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .github/
├── .propel/
│   └── context/
│       └── tasks/
│           └── EP-TECH/
│               ├── us_001/
│               │   ├── us_001.md
│               │   └── task_001_fe_angular_workspace_setup.md  ✓ (completed)
│               └── us_002/
│                   ├── us_002.md
│                   └── task_001_be_dotnet_solution_scaffold.md  ← THIS TASK
├── app/                  ← Angular 18 SPA (created by us_001/task_001)
├── BRD Unified Patient Acces.md
└── README.md
```

*The `server/` .NET 9 solution folder does not yet exist. It will be created as part of this task.*

## Expected Changes

| Action | File Path                                                                 | Description                                                       |
|--------|---------------------------------------------------------------------------|-------------------------------------------------------------------|
| CREATE | `server/Propel.sln`                                                       | .NET 9 solution file referencing all eight projects               |
| CREATE | `server/Propel.Api.Gateway/Propel.Api.Gateway.csproj`                     | Host API project; references all 7 module projects                |
| CREATE | `server/Propel.Api.Gateway/Program.cs`                                    | Service registration, middleware pipeline, Swagger, EF Core, Redis, rate limiting |
| CREATE | `server/Propel.Api.Gateway/appsettings.json`                              | Connection strings (PostgreSQL, Redis), JWT settings (placeholder values) |
| CREATE | `server/Propel.Api.Gateway/Middleware/CorrelationIdMiddleware.cs`         | Injects/propagates `X-Correlation-ID` on every request            |
| CREATE | `server/Propel.Api.Gateway/Middleware/RbacMiddleware.cs`                  | RBAC stub; reads JWT claims, logs for observability               |
| CREATE | `server/Propel.Modules.Auth/`                                             | Auth module: Commands/, Queries/, Handlers/, Validators/ folders + MediatR sample handler |
| CREATE | `server/Propel.Modules.Patient/`                                          | Patient module: same structure                                    |
| CREATE | `server/Propel.Modules.Appointment/`                                      | Appointment module: same structure                                |
| CREATE | `server/Propel.Modules.Clinical/`                                         | Clinical module: same structure                                   |
| CREATE | `server/Propel.Modules.AI/`                                               | AI module: same structure                                         |
| CREATE | `server/Propel.Modules.Notification/`                                     | Notification module: same structure                               |
| CREATE | `server/Propel.Modules.Admin/`                                            | Admin module: same structure                                      |

## External References

- [.NET 9 ASP.NET Core Web API — Getting Started](https://learn.microsoft.com/en-us/aspnet/core/tutorials/first-web-api?view=aspnetcore-9.0)
- [MediatR 12.x — GitHub & NuGet](https://github.com/jbogard/MediatR)
- [MediatR — CQRS with ASP.NET Core](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads)
- [FluentValidation 11.x — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [ASP.NET Core Rate Limiting (.NET 7+)](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-9.0)
- [Swashbuckle/Swagger JWT Bearer config](https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-9.0)
- [Entity Framework Core 9 — Getting Started](https://learn.microsoft.com/en-us/ef/core/get-started/overview/first-app)
- [Npgsql EF Core Provider](https://www.npgsql.org/efcore/)
- [TR-002: .NET 9 ASP.NET Core Web API modular architecture — design.md#technical-requirements]
- [TR-003: EF Core 9 ORM — design.md#technical-requirements]
- [TR-004: PostgreSQL 16+ — design.md#technical-requirements]
- [TR-005: Upstash Redis — design.md#technical-requirements]
- [TR-006: OpenAPI 3.0 — design.md#technical-requirements]
- [TR-019: MediatR CQRS — design.md#technical-requirements]
- [TR-020: FluentValidation — design.md#technical-requirements]
- [NFR-014: Input validation and injection prevention — design.md#security]
- [NFR-017: Rate limiting on public-facing endpoints — design.md#security]

## Build Commands

```bash
# Navigate to solution root
cd server

# Restore all packages
dotnet restore

# Build entire solution
dotnet build

# Run the API gateway host
dotnet run --project Propel.Api.Gateway

# Test Swagger UI (after dotnet run)
# Open browser: https://localhost:5001/swagger
```

## Implementation Validation Strategy

- [ ] `dotnet build` from `server/` exits with code 0 and reports zero errors and zero warnings across all 8 projects (AC1)
- [ ] Each of the seven module project folders contains `Commands/`, `Queries/`, `Handlers/`, and `Validators/` sub-folders with at least one sample type each (AC2)
- [ ] No module `.csproj` contains a `<ProjectReference>` to another module project — only `Propel.Api.Gateway.csproj` references modules (AC2 edge case)
- [ ] A GET request to any stub endpoint returns a response header `X-Correlation-ID` with a non-empty GUID (AC3)
- [ ] RateLimiter returns HTTP 429 when more than 100 requests are made within a 1-minute window to the same endpoint (AC3)
- [ ] `https://localhost:<port>/swagger` renders the Swagger UI with all seven module controllers listed and a JWT Bearer authorize button visible (AC4)
- [ ] `appsettings.json` contains `ConnectionStrings.DefaultConnection`, `Redis.ConnectionString`, and `Jwt` section keys (TR-003, TR-004, TR-005 scaffolding)
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Create `server/` solution: `dotnet new sln -n Propel`; create `Propel.Api.Gateway` (webapi) and seven module class library projects; `dotnet sln add` all eight projects; add `<ProjectReference>` entries in `Propel.Api.Gateway.csproj` for each module only
- [ ] Install `MediatR` 12.x in all 8 projects; scaffold `Commands/`, `Queries/`, `Handlers/` folders per module with a `Ping<Module>Command : IRequest<string>` and `Ping<Module>CommandHandler`; register `AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(...))` in `Program.cs`
- [ ] Install `FluentValidation.AspNetCore` 11.x in all module projects; scaffold `Validators/` folder per module with a `Ping<Module>CommandValidator : AbstractValidator<Ping<Module>Command>`; register `AddFluentValidationAutoValidation()` and `AddValidatorsFromAssemblies(...)` in `Program.cs`
- [ ] Install `Microsoft.EntityFrameworkCore` 9.x and `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x in the gateway project; create placeholder `AppDbContext : DbContext`; register via `AddDbContext<AppDbContext>(opt => opt.UseNpgsql(...))`; populate `appsettings.json` with connection string placeholders for PostgreSQL, Upstash Redis, and JWT settings
- [ ] Implement `CorrelationIdMiddleware.cs`: read `X-Correlation-ID` request header or generate `Guid.NewGuid().ToString()`; store in `HttpContext.Items["CorrelationId"]`; add to response headers; register `app.UseMiddleware<CorrelationIdMiddleware>()` first in pipeline
- [ ] Implement `RbacMiddleware.cs` pass-through stub reading `HttpContext.User.Claims`; configure `services.AddRateLimiter(...)` with a fixed-window policy (100 req/min); apply `app.UseRateLimiter()` and return structured `{ "correlationId": "...", "error": "Too many requests" }` on HTTP 429
- [ ] Configure `AddSwaggerGen` with `OpenApiInfo`, `AddSecurityDefinition("Bearer", ...)`, and `AddSecurityRequirement(...)`; add one stub `[HttpGet("ping")]` controller per module namespace; verify `/swagger` renders with JWT auth badge
- [ ] Run `dotnet build` (confirm zero errors), then `dotnet run`; verify `X-Correlation-ID` header on response, `/swagger` shows all seven module controllers with JWT Bearer, and no module cross-references exist in `.csproj` files
