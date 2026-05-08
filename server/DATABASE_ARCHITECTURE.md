# Database Management Architecture

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         APPLICATION STARTUP                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  1. Program.cs ConfigureServices()                                       │
│     ├─ Register AppDbContext + IDbContextFactory                        │
│     ├─ Register DatabaseInitializer (Singleton)                         │
│     └─ Register DatabaseInitializerHostedService                        │
│                                                                           │
│  2. DatabaseInitializerHostedService.StartAsync()                       │
│     └─ Calls DatabaseInitializer.InitializeAsync()                     │
│         ├─ Check database connectivity                                  │
│         ├─ Get pending migrations                                       │
│         ├─ Apply migrations (if any)                                    │
│         └─ Log status                                                   │
│                                                                           │
│  3. Program.cs Startup Scope                                             │
│     ├─ Check migration status (detailed logging)                        │
│     ├─ Apply migrations (EF Core Migrate)                               │
│     ├─ Seed reference data                                              │
│     └─ Log completion                                                   │
│                                                                           │
│  4. Map Endpoints                                                        │
│     └─ DatabaseEndpoints.MapDatabaseEndpoints()                         │
│         ├─ GET  /api/database/status                                    │
│         ├─ POST /api/database/migrate                                   │
│         └─ GET  /api/database/health                                    │
│                                                                           │
│  5. Application Ready ✅                                                 │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

## Component Diagram

```
┌───────────────────────────────────────────────────────────────────────────┐
│                              CLIENT LAYER                                  │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────────────┐      │
│  │  HTTP Client    │  │  PowerShell     │  │  Browser             │      │
│  │  (curl, etc)    │  │  Script         │  │  (Swagger UI)        │      │
│  └────────┬────────┘  └────────┬────────┘  └──────────┬───────────┘      │
│           │                    │                       │                   │
│           └────────────────────┴───────────────────────┘                   │
│                                │                                           │
└────────────────────────────────┼───────────────────────────────────────────┘
								 │
								 ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                           API LAYER (ASP.NET Core)                         │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                    DatabaseEndpoints                                 │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  • GET  /api/database/status    → GetDatabaseStatus()              │  │
│  │  • POST /api/database/migrate   → ApplyMigrations()                │  │
│  │  • GET  /api/database/health    → CheckDatabaseHealth()            │  │
│  └──────────────────────────┬──────────────────────────────────────────┘  │
│                             │                                              │
│                             ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                   DatabaseInitializer (Service)                      │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  • InitializeAsync()         - Apply migrations                     │  │
│  │  • CanConnectAsync()         - Check connectivity                   │  │
│  │  • GetStatusAsync()          - Get migration status                 │  │
│  └──────────────────────────┬──────────────────────────────────────────┘  │
│                             │                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │         DatabaseInitializerHostedService (Background)                │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  • StartAsync()              - Run on app startup                   │  │
│  │  • StopAsync()               - Cleanup on shutdown                  │  │
│  └──────────────────────────┬──────────────────────────────────────────┘  │
│                             │                                              │
└─────────────────────────────┼────────────────────────────────────────────┘
							  │
							  ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                      DATA ACCESS LAYER (EF Core)                          │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                  IDbContextFactory<AppDbContext>                     │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  • CreateDbContext()         - Create isolated DB context           │  │
│  └──────────────────────────┬──────────────────────────────────────────┘  │
│                             │                                              │
│                             ▼                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                        AppDbContext                                  │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  DbSets: Patients, Users, Appointments, etc.                        │  │
│  │  • Database.CanConnectAsync()                                       │  │
│  │  • Database.GetPendingMigrationsAsync()                             │  │
│  │  • Database.GetAppliedMigrationsAsync()                             │  │
│  │  • Database.MigrateAsync()                                          │  │
│  └──────────────────────────┬──────────────────────────────────────────┘  │
│                             │                                              │
└─────────────────────────────┼────────────────────────────────────────────┘
							  │
							  ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                         DATABASE LAYER (PostgreSQL)                        │
├───────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │                      PostgreSQL Database                             │  │
│  ├─────────────────────────────────────────────────────────────────────┤  │
│  │  • Tables: patients, users, appointments, etc.                      │  │
│  │  • __EFMigrationsHistory table (tracks applied migrations)          │  │
│  │  • Extensions: pgcrypto, pgvector (optional)                        │  │
│  │  • Connection Pool (50 max, 5 min)                                  │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└───────────────────────────────────────────────────────────────────────────┘
```

## Data Flow - Check Status

```
┌─────────┐      GET /api/database/status      ┌──────────────────┐
│ Client  │ ─────────────────────────────────> │ DatabaseEndpoints│
└─────────┘                                     └────────┬─────────┘
														│
														▼
											   ┌──────────────────┐
											   │ DatabaseInitializer│
											   └────────┬─────────┘
														│
										  ┌─────────────┴──────────────┐
										  │                             │
										  ▼                             ▼
								 ┌─────────────────┐      ┌─────────────────────┐
								 │ AppDbContext    │      │ AppDbContext        │
								 └────────┬────────┘      └────────┬────────────┘
										  │                         │
										  ▼                         ▼
						   GetAppliedMigrationsAsync()  GetPendingMigrationsAsync()
										  │                         │
										  └─────────────┬───────────┘
														│
														▼
											  ┌──────────────────┐
											  │ DatabaseStatus   │
											  │ (Response DTO)   │
											  └────────┬─────────┘
														│
┌─────────┐      JSON Response (200 OK)               │
│ Client  │ <──────────────────────────────────────────┘
└─────────┘
```

## Data Flow - Apply Migration

```
┌─────────┐     POST /api/database/migrate     ┌──────────────────┐
│ Client  │ ─────────────────────────────────> │ DatabaseEndpoints│
└─────────┘                                     └────────┬─────────┘
														│
														▼
											   ┌──────────────────┐
											   │ DatabaseInitializer│
											   └────────┬─────────┘
														│
														▼
											   ┌──────────────────┐
											   │ GetStatusAsync() │
											   │ (check pending)  │
											   └────────┬─────────┘
														│
														▼
											   ┌──────────────────┐
											   │ AppDbContext     │
											   └────────┬─────────┘
														│
														▼
										  Database.MigrateAsync()
														│
														▼
										  ┌──────────────────────────┐
										  │ PostgreSQL               │
										  │ • Execute migration SQL  │
										  │ • Update __EFMigrations  │
										  └──────────┬───────────────┘
														│
┌─────────┐      JSON Response (200 OK)               │
│ Client  │ <──────────────────────────────────────────┘
│         │      { success: true,
│         │        message: "Applied 2 migrations",
│         │        migrationsApplied: 2 }
└─────────┘
```

## Startup Sequence

```
Application Start
	│
	├─ 1. ConfigureServices
	│     ├─ AddDbContextFactory<AppDbContext>
	│     ├─ AddSingleton<DatabaseInitializer>
	│     └─ AddHostedService<DatabaseInitializerHostedService>
	│
	├─ 2. Build WebApplication
	│
	├─ 3. IHostedService.StartAsync() [Background]
	│     └─ DatabaseInitializerHostedService
	│         └─ DatabaseInitializer.InitializeAsync()
	│             ├─ Check connectivity
	│             ├─ Check pending migrations
	│             ├─ Apply migrations
	│             └─ Log status
	│
	├─ 4. Configure Middleware Pipeline
	│     ├─ UseExceptionHandler
	│     ├─ UseHttpsRedirection
	│     ├─ UseRateLimiter
	│     ├─ UseCors
	│     ├─ UseAuthentication
	│     └─ UseAuthorization
	│
	├─ 5. Map Endpoints
	│     ├─ MapControllers()
	│     ├─ DatabaseEndpoints.MapDatabaseEndpoints()
	│     ├─ MapHealthChecks("/health")
	│     └─ MapHealthChecks("/health/live")
	│
	├─ 6. Startup Scope (Detailed Migration)
	│     ├─ Get DatabaseInitializer
	│     ├─ Get AppDbContext
	│     ├─ Check status (detailed logging)
	│     ├─ Apply migrations
	│     ├─ Seed reference data
	│     └─ Log completion
	│
	└─ 7. app.Run() ✅
		  Application Ready
```

## Error Handling Flow

```
					┌─────────────────────────┐
					│  Database Operation     │
					└───────────┬─────────────┘
								│
								▼
					┌───────────────────────┐
					│  Try-Catch Block      │
					└───────────┬───────────┘
								│
				┌───────────────┴───────────────┐
				│                               │
				▼                               ▼
		┌──────────────┐              ┌─────────────────┐
		│   Success    │              │     Exception   │
		└──────┬───────┘              └────────┬────────┘
			   │                               │
			   ▼                               ▼
	┌──────────────────┐           ┌────────────────────┐
	│  Log Success     │           │  Log Error         │
	│  Return Result   │           │  Return Error DTO  │
	└──────────────────┘           └────────┬───────────┘
											│
											▼
								   ┌─────────────────────┐
								   │  Client receives    │
								   │  ProblemDetails     │
								   │  (RFC 7807)         │
								   └─────────────────────┘
```

## Migration States

```
┌─────────────────────────────────────────────────────────────────┐
│                    Migration Lifecycle                          │
└─────────────────────────────────────────────────────────────────┘

  Created (dotnet ef migrations add)
	  │
	  ├─ Files generated in /Migrations
	  │   ├─ YYYYMMDDHHMMSS_MigrationName.cs
	  │   ├─ YYYYMMDDHHMMSS_MigrationName.Designer.cs
	  │   └─ AppDbContextModelSnapshot.cs (updated)
	  │
	  ▼
  Pending (Not yet applied to database)
	  │
	  ├─ Detected by GetPendingMigrationsAsync()
	  ├─ Shown in status endpoint
	  │
	  ▼
  Applying (MigrateAsync in progress)
	  │
	  ├─ SQL generated from Up() method
	  ├─ Transaction started
	  ├─ SQL executed
	  │
	  ▼
  Applied (Successfully recorded in __EFMigrationsHistory)
	  │
	  ├─ Detected by GetAppliedMigrationsAsync()
	  ├─ Cannot be removed (only rolled back)
	  │
	  └─ Database schema updated ✅
```

## Key Design Patterns

1. **Factory Pattern** - `IDbContextFactory<AppDbContext>` for isolated contexts
2. **Hosted Service Pattern** - Background initialization on startup
3. **Dependency Injection** - All components registered in DI container
4. **Repository Pattern** - Separation of data access concerns
5. **REST API Pattern** - Standard HTTP methods for database operations
6. **Health Check Pattern** - Multiple endpoints for different monitoring needs
7. **Command Pattern** - PowerShell script encapsulates operations

## Technology Stack

```
┌─────────────────────────────────────────────────────────────────┐
│  .NET 10                                                         │
│    └─ ASP.NET Core (Web API)                                    │
│        └─ Entity Framework Core 9                               │
│            └─ Npgsql (PostgreSQL provider)                      │
│                └─ PostgreSQL 16                                 │
└─────────────────────────────────────────────────────────────────┘

Supporting:
  • Serilog (Structured logging)
  • Swagger/OpenAPI (API documentation)
  • PowerShell 7+ (Management script)
```
