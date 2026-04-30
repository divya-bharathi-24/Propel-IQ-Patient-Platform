using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Endpoints;
using Propel.Api.Gateway.HealthChecks;
using Propel.Api.Gateway.Infrastructure;
using Propel.Api.Gateway.Infrastructure.BackgroundServices;
using Propel.Api.Gateway.Infrastructure.Behaviors;
using Propel.Api.Gateway.Infrastructure.Cache;
using Propel.Api.Gateway.Infrastructure.Documents;
using Propel.Api.Gateway.Infrastructure.Email;
using Propel.Api.Gateway.Infrastructure.Filters;
using Propel.Api.Gateway.Infrastructure.HealthChecks;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Api.Gateway.Infrastructure.Notifications;
using Propel.Api.Gateway.Infrastructure.Pdf;
using Propel.Api.Gateway.Infrastructure.Persistence.AuditLog;
using Propel.Api.Gateway.Infrastructure.ReAuth;
using Propel.Api.Gateway.Infrastructure.Repositories;
using Propel.Api.Gateway.Infrastructure.Security;
using Propel.Api.Gateway.Infrastructure.Services;
using Propel.Api.Gateway.Infrastructure.Session;
using Propel.Api.Gateway.Infrastructure.Sms;
using Propel.Api.Gateway.Middleware;
using Propel.Api.Gateway.Security;
using Propel.Domain.Interfaces;
// ── Module assembly references ────────────────────────────────────────────────
using Propel.Modules.Admin.Commands;
using Propel.Modules.AI.Commands;
using Propel.Modules.AI.Options;
using Propel.Modules.AI.Registration;
using Propel.Modules.AI.Services;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Configuration;
using Propel.Modules.Appointment.Infrastructure;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Auth.Services;
using Propel.Modules.Calendar.BackgroundServices;
using Propel.Modules.Calendar.Commands;
using Propel.Modules.Calendar.Interfaces;
using Propel.Modules.Calendar.Options;
using Propel.Modules.Calendar.Services;
using Propel.Modules.Clinical.Commands;
using Propel.Modules.Notification.Commands;
using Propel.Modules.Notification.Dispatchers;
using Propel.Modules.Notification.Notifiers;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Services;
using Propel.Modules.Queue.Commands;
using Propel.Modules.Risk.Commands;
using Propel.Modules.Risk.Interfaces;
using Propel.Modules.Risk.Services;
using Serilog;
using StackExchange.Redis;
using System.Security.Authentication;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ── Kestrel TLS 1.2+ enforcement + connection/body limits (NFR-005, NFR-010, NFR-019, EP-011/us_053/task_002) ─
// TLS: Restricts Kestrel to TLS 1.2 and TLS 1.3 for direct HTTPS scenarios (local dev).
// On Railway, TLS is terminated at the platform ingress; this setting adds defence-in-depth.
// Connection limits: sized for 100 concurrent users with 2× headroom (AC-2, NFR-010).
// MaxRequestBodySize: 1 MB global default; per-action [RequestSizeLimit] overrides for upload endpoints.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });

    // Global request body cap — upload endpoints override this with [RequestSizeLimit] (NFR-019).
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB default for non-upload endpoints

    // Connection limits for 100 concurrent users: 2× headroom (NFR-010, AC-2).
    options.Limits.MaxConcurrentConnections = 200;
    options.Limits.MaxConcurrentUpgradedConnections = 50; // WebSocket budget

    // Keep-alive and header timeouts: balanced for long-lived upload streams (NFR-019).
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// ── Serilog structured logging (NFR-013) ─────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

// ── Startup environment validation guard (OWASP A05: Security Misconfiguration) ──
// Fail fast on startup if required env vars are missing rather than surfacing
// cryptic runtime errors deep in request handling.
static void RequireEnvVar(IConfiguration config, string key)
{
    if (string.IsNullOrWhiteSpace(config[key]))
        throw new InvalidOperationException(
            $"Required environment variable '{key}' is not configured. " +
            "Ensure it is registered in Railway (backend) or Netlify (frontend) environment variables.");
}

// Only enforce strict environment variable validation in Production
if (!builder.Environment.IsDevelopment())
{
    RequireEnvVar(configuration, "DATABASE_URL");
    RequireEnvVar(configuration, "REDIS_URL");
    RequireEnvVar(configuration, "Jwt:SecretKey");
    RequireEnvVar(configuration, "CORS:AllowedOrigins");
    // OWASP A02 — no startup without an encryption key in production (AC-4 / NFR-004)
    RequireEnvVar(configuration, "ENCRYPTION_KEY");
}

// ── Controllers (GlobalExceptionFilter registered globally; ModelStateInvalidFilter
// suppressed to prevent double-error responses when FluentValidation auto-validation fires) ──
builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
    // Suppress the default 400 response from ModelStateInvalidFilter so that
    // GlobalExceptionFilter is the sole owner of validation error formatting (AC-2).
    var invalidModelStateFilter = options.Filters
        .OfType<Microsoft.AspNetCore.Mvc.Infrastructure.ModelStateInvalidFilter>()
        .FirstOrDefault();
    if (invalidModelStateFilter is not null)
        options.Filters.Remove(invalidModelStateFilter);
})
.ConfigureApiBehaviorOptions(apiBehaviorOptions =>
{
    // Disable the automatic 400 shortcut so FluentValidation ValidationException flows
    // through GlobalExceptionFilter instead of being swallowed by ProblemDetails middleware.
    apiBehaviorOptions.SuppressModelStateInvalidFilter = true;
});

// ── MediatR (all 8 module assemblies + gateway) ───────────────────────────────
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(
        typeof(PingAuthCommand).Assembly,
        typeof(PingPatientCommand).Assembly,
        typeof(PingAppointmentCommand).Assembly,
        typeof(PingClinicalCommand).Assembly,
        typeof(PingAICommand).Assembly,
        typeof(PingNotificationCommand).Assembly,
        typeof(PingAdminCommand).Assembly,
        typeof(PingQueueCommand).Assembly,    // Propel.Modules.Queue — US_027
        typeof(PingRiskCommand).Assembly,     // Propel.Modules.Risk — us_031 (NoShow Risk Engine)
        typeof(PingCalendarCommand).Assembly, // Propel.Modules.Calendar — EP-007/us_035 (Google Calendar sync)
        typeof(Program).Assembly      // Propel.Api.Gateway — BookingConfirmedEventHandler (US_021, TASK_002), BookingConfirmedRiskHandler (us_031)
    );
});

// ── EP-011/us_051/task_002 — MediatR pipeline behaviors (ordered: Log → Validate → Perf) ──
// Registered as open-generic transients so a new instance is created per request type.
// Pipeline execution order matches registration order (first registered = outermost).
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

// ── FluentValidation (all 7 module assemblies) ────────────────────────────────
// DisableDataAnnotationsValidation = true: prevents DataAnnotations from running
// alongside FluentValidation, avoiding duplicate/conflicting 400 responses (AC-2).
builder.Services.AddFluentValidationAutoValidation(cfg =>
    cfg.DisableDataAnnotationsValidation = true);
builder.Services.AddValidatorsFromAssemblies([
    typeof(PingAuthCommand).Assembly,
    typeof(PingPatientCommand).Assembly,
    typeof(PingAppointmentCommand).Assembly,
    typeof(PingClinicalCommand).Assembly,
    typeof(PingAICommand).Assembly,
    typeof(PingNotificationCommand).Assembly,
    typeof(PingAdminCommand).Assembly,
    typeof(PingQueueCommand).Assembly,   // Propel.Modules.Queue — US_027
    typeof(PingRiskCommand).Assembly,    // Propel.Modules.Risk — us_031
    typeof(PingCalendarCommand).Assembly, // Propel.Modules.Calendar — EP-007/us_035
    typeof(Program).Assembly             // Propel.Api.Gateway — US_038 (UploadClinicalDocuments), US_039 (UploadStaffClinicalNote)
]);

// ── Entity Framework Core 9 + PostgreSQL ─────────────────────────────────────
// DATABASE_URL env var (Railway standard) takes precedence over appsettings ConnectionStrings.
var connectionString = configuration["DATABASE_URL"]
    ?? configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DATABASE_URL is required.");

// ── Npgsql connection pool tuning for 100 concurrent users (NFR-010, AC-2, EP-011/us_053/task_002) ──
// Little's Law: L = λW → at 100 RPS with 50ms avg query time ≈ 5 concurrent connections.
// Pool of 50 provides 10× headroom; Minimum Pool Size=5 pre-warms connections on startup.
// Append pool parameters only if DATABASE_URL does not already contain them.
if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
{
    var poolParams = ";Maximum Pool Size=50;Minimum Pool Size=5;Connection Idle Lifetime=300;Connection Pruning Interval=60";
    connectionString = connectionString.TrimEnd(';') + poolParams;
}

// UseVector() registers the pgvector type handler so the Npgsql driver can
// serialize/deserialize float[] ↔ vector columns at runtime (US_040, task_002, AC-1).
// TEMPORARY: pgvector disabled until extension is installed (uncomment after running docker-compose up)
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
// dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

// IDbContextFactory<AppDbContext> — used by AuditLogRepository to create isolated DbContext
// scopes per audit write, preventing audit entries from being rolled back by outer business
// transactions (US_013, AD-7). Also provides pooled DbContext instances for request-scoped
// repositories. Configured with Singleton options lifetime to prevent scoped service consumption
// from singleton factory (EF Core DI pattern).
builder.Services.AddDbContextFactory<AppDbContext>(
    (serviceProvider, opt) =>
    {
        // TEMPORARY: pgvector disabled until extension is installed (uncomment after running docker-compose up)
        // UseVector() registers EF Core type mappings for Pgvector.Vector ↔ vector(N) columns (US_040, task_002, AC-1).
        opt.UseNpgsql(dataSource /*, o => o.UseVector()*/)
           .UseSnakeCaseNamingConvention()
           .UseApplicationServiceProvider(serviceProvider)
           .ConfigureWarnings(warnings =>
               warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    },
    ServiceLifetime.Singleton);

// Register scoped AppDbContext that uses the factory — this allows existing repository
// constructors that inject AppDbContext directly to continue working unchanged.
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    return factory.CreateDbContext();
});

// ── NpgsqlDataSource singleton — shared across EF Core and raw Npgsql commands ─
// Required by PgcryptoEncryptionService which executes raw pgp_sym_encrypt / pgp_sym_decrypt
// scalar queries outside of the EF Core pipeline (task_002, AC-4).
builder.Services.AddSingleton(dataSource);

// ── AES-256 Encryption Service (pgcrypto) ─────────────────────────────────────
// PgcryptoEncryptionService wraps pgcrypto's pgp_sym_encrypt/pgp_sym_decrypt via raw Npgsql.
// ENCRYPTION_KEY is validated at construction (fail-fast, OWASP A02 / AC-4 / NFR-004).
// The singleton is injected into PHI-aware repositories (Patient, Clinical).
builder.Services.AddSingleton<IEncryptionService, PgcryptoEncryptionService>();

// ── Redis (StackExchange.Redis) — graceful degradation per NFR-018 / AC4 ─────
// DEVELOPMENT MODE: Redis is DISABLED. Using in-memory session storage.
// PRODUCTION MODE: Redis is REQUIRED for session management.
if (builder.Environment.IsDevelopment())
{
    Log.Warning("DEVELOPMENT MODE: Redis is disabled. Using IN-MEMORY session storage. Sessions will be lost on restart!");
    
    // Register a dummy IConnectionMultiplexer to satisfy DI dependencies
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => 
        throw new InvalidOperationException("Redis is disabled in development. Use in-memory session service."));
}
else
{
    // Production: Redis is required
    var rawRedisConn = configuration["REDIS_URL"] 
        ?? configuration["Redis:ConnectionString"] 
        ?? throw new InvalidOperationException("REDIS_URL is required in production.");

    var redisOptions = ConfigurationOptions.Parse(rawRedisConn);
    redisOptions.AbortOnConnectFail = false;
    redisOptions.ConnectRetry = 3;
    redisOptions.ReconnectRetryPolicy = new LinearRetry(2_000);

    try
    {
        var redisMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
        
        // Test connection
        var testDb = redisMultiplexer.GetDatabase();
        testDb.Ping();
        
        Log.Information("Redis connected successfully at {Endpoint}", rawRedisConn);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            "Redis connection is required in production. Please ensure Redis is available.", ex);
    }
}

// ── ASP.NET Core Data Protection — AES-256 key ring for PHI encryption (NFR-004, NFR-013) ───
// Key lifetime: 90-day rotation. All historical keys are retained so previously
// encrypted DB values can always be decrypted after rotation.
// Local dev: keys persist to file system. Production: Upstash Redis key store.
var dpBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("propeliq-platform")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

if (builder.Environment.IsDevelopment())
{
    // Local dev: persist keys to a configurable file system path (never ephemeral in-memory)
    var keyPath = configuration["DataProtection:KeyPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys");
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    Log.Information("Data Protection: Persisting keys to {KeyPath}", keyPath);
}
else
{
    // Production: persist keys to Upstash Redis
    var rawRedisConn = configuration["REDIS_URL"] 
        ?? configuration["Redis:ConnectionString"] 
        ?? throw new InvalidOperationException("REDIS_URL is required for Data Protection in production.");
    
    var redisOptions = ConfigurationOptions.Parse(rawRedisConn);
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
    dpBuilder.PersistKeysToStackExchangeRedis(redisMultiplexer, "DataProtection-Keys");
    Log.Information("Data Protection: Persisting keys to Redis");
}

// ── Argon2id password hasher (NFR-008, DRY) — replaces direct Argon2 calls in handlers ──
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

// ── PHI encryption service (NFR-004, NFR-013) — AES-256 via Data Protection key ring ──
builder.Services.AddSingleton<IPhiEncryptionService, AesGcmPhiEncryptionService>();

// ── Clinical document encryption service (US_038, US_039, NFR-004, FR-043) ────
// Uses a dedicated Data Protection purpose string "ClinicalDocuments.v1" isolated from PHI keys.
builder.Services.AddSingleton<IFileEncryptionService, DataProtectionFileEncryptionService>();

// ── Clinical document storage service (US_038, US_039) ───────────────────────
// DocumentStorage:StoragePath configured in appsettings.json; defaults to ./document-storage.
builder.Services.Configure<DocumentStorageSettings>(
    builder.Configuration.GetSection("DocumentStorage"));
builder.Services.AddSingleton<IDocumentStorageService, LocalDocumentStorageService>();

// ── PDF streaming storage service (EP-011/us_053/task_002, NFR-019) ──────────
// PdfStreamingStorageService streams large uploads in 4 MB chunks without buffering the
// full file in memory. Used by Staff AI-document upload flows where per-file encryption
// is deferred to the OS / Azure Blob Storage server-side encryption (Phase 1/Phase 2).
builder.Services.AddScoped<IPdfStreamingStorageService, PdfStreamingStorageService>();

// ── JWT Authentication ────────────────────────────────────────────────────────
// Jwt:SecretKey is set via env var JWT__SecretKey (Railway). Already validated above.
var jwtSecret = configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey configuration is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidAudience = configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

// ── ASP.NET Core Health Checks — all 7 external dependencies (EP-011/us_052, AC-1, NFR-003) ──
// No authentication required; /health is a liveness probe, not a data endpoint.
// Each check has a 5-second individual timeout so a single slow dependency cannot block
// the full health response. PostgreSQL failure → HTTP 503; all other failures → Degraded (HTTP 200).
// PgcryptoHealthCheck verifies the pgcrypto extension is active before traffic is accepted (task_002, AC-4).
builder.Services.AddHealthChecks()
    .AddCheck<PgcryptoHealthCheck>("pgcrypto",
        tags: ["db", "critical"],
        timeout: TimeSpan.FromSeconds(5))
    // PostgreSQL — critical: failure returns HTTP 503 and sets Redis db_down flag (AC-1 edge-case).
    .AddCheck<PostgreSqlHealthCheck>("postgresql",
        tags: ["db", "critical"],
        timeout: TimeSpan.FromSeconds(5))
    // Redis — Degraded (not Unhealthy) on failure: failureStatus overrides exception-path status
    // when Redis is intentionally disabled in development or unreachable in production (NFR-018).
    .AddCheck<RedisHealthCheck>("redis",
        failureStatus: HealthStatus.Degraded,
        tags: ["cache"],
        timeout: TimeSpan.FromSeconds(5))
    // Email, SMS, AI, and calendar providers — Degraded on failure; non-critical to core workflows (AG-6).
    .AddCheck<SendGridHealthCheck>("sendgrid",
        failureStatus: HealthStatus.Degraded,
        tags: ["email", "degradable"],
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<TwilioHealthCheck>("twilio",
        failureStatus: HealthStatus.Degraded,
        tags: ["sms", "degradable"],
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<OpenAiHealthCheck>("openai",
        failureStatus: HealthStatus.Degraded,
        tags: ["ai", "degradable"],
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<GoogleCalendarHealthCheck>("google-calendar",
        failureStatus: HealthStatus.Degraded,
        tags: ["calendar", "degradable"],
        timeout: TimeSpan.FromSeconds(5))
    .AddCheck<MicrosoftGraphHealthCheck>("microsoft-graph",
        failureStatus: HealthStatus.Degraded,
        tags: ["calendar", "degradable"],
        timeout: TimeSpan.FromSeconds(5));

// ── Rate Limiting — named policies defined in RateLimitingPolicies (NFR-017, AC-1) ──────────
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    // Delegate all policy definitions to the centralised static class.
    RateLimitingPolicies.Configure(rateLimiterOptions, configuration);

    // ── 429 rejection handler: Retry-After header + Serilog warning + audit (AC-1) ─
    rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
    {
        // Determine Retry-After from lease metadata; fall back to 60 s if not available.
        int retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        // Structured Serilog warning — no PII beyond IP (OWASP A09, NFR-013).
        Log.Warning(
            "RateLimit exceeded: {Endpoint} from {IP} — retry after {RetryAfterSeconds}s",
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            retryAfterSeconds);

        // US_013 AC-2 (RateLimitBlock): write audit entry for IP-based rate-limit blocks.
        // UserId is NULL — user identity is not yet established at the rate-limiter stage.
        try
        {
            var auditSvc = context.HttpContext.RequestServices.GetService<AuditLogService>();
            if (auditSvc is not null)
            {
                await auditSvc.AppendAsync(new Propel.Domain.Entities.AuditLog
                {
                    Id         = Guid.NewGuid(),
                    UserId     = null,
                    Action     = AuthAuditActions.RateLimitBlock,
                    EntityType = "RateLimiter",
                    EntityId   = Guid.Empty,
                    IpAddress  = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    Timestamp  = DateTime.UtcNow
                }, cancellationToken);
            }
        }
        catch
        {
            // Audit failure must never interrupt the 429 response (edge case spec).
        }

        var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? string.Empty;
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { correlationId, error = "Too many requests", retryAfterSeconds },
            cancellationToken);
    };
});

// ── Swagger / OpenAPI 3.0 with JWT Bearer ────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Propel IQ API",
        Version = "v1",
        Description = "Unified Patient Access & Clinical Intelligence Platform — API Gateway"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter 'Bearer {token}'",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// ── HSTS — strict transport security (NFR-005, AC-4) ───────────────────────────
// 2-year max-age (730 days) with includeSubDomains and preload per OWASP TLS cheat sheet.
// UseHsts() in the pipeline sends the Strict-Transport-Security header on every HTTPS response.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(730);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// ── CORS — allow requests only from the Netlify-hosted frontend (AC-4) ────────
// CORS:AllowedOrigins is set via env var CORS__AllowedOrigins (Railway).
// No wildcard is permitted; exact origin must be specified (OWASP A05).
var allowedOrigins = configuration["CORS:AllowedOrigins"]
    ?? throw new InvalidOperationException(
        "CORS:AllowedOrigins environment variable is required. " +
        "Set CORS__AllowedOrigins to the Netlify frontend URL (e.g., https://propeliq.netlify.app).");

// Split comma-separated origins to support multiple origins in development/production
var originsArray = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("NetlifyPolicy", policy =>
    {
        policy.WithOrigins(originsArray)
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
              .WithExposedHeaders("X-Correlation-Id", "Retry-After", "X-Degraded")
              .AllowCredentials()
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// ── Middleware singletons ─────────────────────────────────────────────────────
builder.Services.AddTransient<CorrelationIdMiddleware>();
builder.Services.AddTransient<RequestLoggingMiddleware>();
builder.Services.AddTransient<RbacMiddleware>();
builder.Services.AddTransient<ExceptionHandlingMiddleware>();
builder.Services.AddTransient<SessionAliveMiddleware>();

// ── EP-011/us_051/task_001 — Correlation ID accessor (ICorrelationIdAccessor) ─
// Scoped so it shares the DI scope with MediatR pipeline behaviors (task_002).
builder.Services.AddScoped<ICorrelationIdAccessor, HttpContextCorrelationIdAccessor>();

// ── Registration / Verification repositories (us_010 task_002) ───────────────
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// ── US_047 — Audit log read repository (task_002) ─────────────────────────────
// EfAuditLogReadRepository exposes only read methods; no write surface (AD-7, FR-059).
builder.Services.AddScoped<IAuditLogReadRepository, EfAuditLogReadRepository>();

// ── US_016 — Patient dashboard aggregation repository (TASK_002) ──────────────
builder.Services.AddScoped<IPatientDashboardRepository, PatientDashboardRepository>();

// ── US_017 — Intake edit repository (TASK_002) ────────────────────────────────
builder.Services.AddScoped<IIntakeRepository, IntakeRepository>();

// ── US_018 — Slot availability cache and repository (TASK_002) ────────────────
// SlotConfiguration POCO bound from appsettings.json "SlotConfiguration" section.
builder.Services.Configure<SlotConfiguration>(configuration.GetSection("SlotConfiguration"));
// Appointment slot repository: EF Core fallback for slot availability queries.
builder.Services.AddScoped<IAppointmentSlotRepository, AppointmentSlotRepository>();
// ISlotCacheService: NullSlotCacheService in development (Redis disabled), RedisSlotCacheService in production.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISlotCacheService, NullSlotCacheService>();
    Log.Information("SlotCacheService: Using NULL (no-op) cache in development mode.");
}
else
{
    builder.Services.AddScoped<ISlotCacheService, RedisSlotCacheService>();
    Log.Information("SlotCacheService: Using Redis cache in production mode.");
}

// ── US_013 — Audit logging service (task_001): retry-once wrapper + Critical alert ──
// AuditLogService is scoped so it shares the DI scope with request handlers.
// SessionExpirySubscriberService creates its own scope via IServiceScopeFactory.
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddHttpContextAccessor();

// ── US_019 — Appointment booking repositories and services (task_002) ─────────
// AppointmentBookingRepository: EF Core writes for Appointment, InsuranceValidation, WaitlistEntry.
builder.Services.AddScoped<IAppointmentBookingRepository, AppointmentBookingRepository>();
// InsuranceSoftCheckService: inline insurance soft-check against DummyInsurers seed table.
builder.Services.AddScoped<IInsuranceSoftCheckService, InsuranceSoftCheckService>();

// ── US_023 — Waitlist enrollment repository (task_002) ────────────────────────
// WaitlistRepository: EF Core read/update for WaitlistEntry (GetMyWaitlist, CancelPreference).
builder.Services.AddScoped<IWaitlistRepository, WaitlistRepository>();

// ── US_026 — Staff walk-in booking repository (task_002) ──────────────────────
// StaffWalkInRepository: patient search + atomic walk-in booking (Patient? + Appointment + QueueEntry).
builder.Services.AddScoped<IStaffWalkInRepository, StaffWalkInRepository>();

// ── US_027 — Queue repository (same-day queue management) ────────────────────
builder.Services.AddScoped<IQueueRepository, QueueRepository>();

// ── US_022 — Insurance pre-check endpoint (task_002) ──────────────────────────
// DummyInsurersRepository: thin EF Core read-only query for the DummyInsurers seed table.
builder.Services.AddScoped<IDummyInsurersRepository, DummyInsurersRepository>();
// InsuranceSoftCheckClassifier: pure classification service (no DB dependency) — extracts
// Incomplete detection and guidance text constants for testability (NFR-013).
builder.Services.AddScoped<InsuranceSoftCheckClassifier>();

// ── US_020 — Appointment cancellation background task queue (task_002) ────────
// BackgroundTaskQueue: singleton channel-backed queue for fire-and-forget work items.
builder.Services.AddSingleton<Propel.Modules.Appointment.Infrastructure.IBackgroundTaskQueue, BackgroundTaskQueue>();
// QueuedHostedService: long-running hosted service that drains the task queue.
builder.Services.AddHostedService<QueuedHostedService>();
// RevokeCalendarSyncBackgroundTask: enqueues CalendarSync revocation after cancellation (AC-2, NFR-018).
builder.Services.AddScoped<Propel.Modules.Appointment.Infrastructure.ICalendarSyncRevocationService, RevokeCalendarSyncBackgroundTask>();

// ── US_013 — Session expiry background service (task_001) ─────────────────────
// Only register in production where Redis is available
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<SessionExpirySubscriberService>();
    Log.Information("SessionExpirySubscriberService: ENABLED (production mode)");
}
else
{
    Log.Warning("SessionExpirySubscriberService: DISABLED (development mode - Redis not available)");
}

// ── US_011 — Auth services and refresh-token repository (task_002) ────────────
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();

// Session service: In-memory for development, Redis for production
if (builder.Environment.IsDevelopment())
{
    // Development: Always use in-memory session service (Redis disabled)
    builder.Services.AddScoped<IRedisSessionService, InMemoryRedisSessionService>();
    Log.Information("Session service: Using IN-MEMORY storage (development mode)");
}
else
{
    // Production: Always use Redis session service (required)
    builder.Services.AddScoped<IRedisSessionService, RedisSessionService>();
    Log.Information("Session service: Using REDIS storage (production mode)");
}

// ── US_012 — Admin account management repositories (task_002) ─────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICredentialSetupTokenRepository, CredentialSetupTokenRepository>();

// ── US_045 — Admin user CRUD API (task_002) ────────────────────────────────────
// ICredentialEmailService: SendGrid implementation returning bool (graceful degradation, NFR-018).
builder.Services.AddTransient<ICredentialEmailService, SendGridCredentialEmailService>();
// ISessionInvalidationService: delegates to IRedisSessionService.DeleteAllUserSessionsAsync (AD-9).
builder.Services.AddScoped<ISessionInvalidationService, RedisSessionInvalidationService>();
Log.Information("Admin user CRUD services registered (US_045, task_002).");

// ── US_046 — Re-auth token store for Admin elevation and destructive actions (task_002) ────
// Development: in-memory store (Redis disabled); Production: Redis-backed single-use store.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IReAuthTokenStore, InMemoryReAuthTokenStore>();
    Log.Information("ReAuthTokenStore: Using IN-MEMORY store (development mode).");
}
else
{
    builder.Services.AddSingleton<IReAuthTokenStore, RedisReAuthTokenStore>();
    Log.Information("ReAuthTokenStore: Using REDIS store (production mode).");
}
Log.Information("Admin re-auth role assignment services registered (US_046, task_002).");

// ── Email service — SendGrid (us_010 task_002, NFR-018) ──────────────────────
builder.Services.AddTransient<IEmailService, SendGridEmailService>();

// ── US_025 — Slot-swap dual-channel notification services (task_001) ──────────
// ISmsService: Twilio SDK wrapper for SMS dispatch (NFR-018 graceful degradation).
builder.Services.AddTransient<ISmsService, TwilioSmsService>();
// INotificationRepository: INSERT-only EF Core repository for Notification records (AC-2).
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

// ── US_021 — PDF confirmation service (task_001) ──────────────────────────────
// IPdfConfirmationService: scoped QuestPDF in-memory PDF generation.
builder.Services.AddScoped<IPdfConfirmationService, QuestPdfConfirmationService>();

// ── US_020/US_021 — Appointment confirmation email service adapter (task_003) ─
// IAppointmentConfirmationEmailService: thin adapter combining PDF generation + email dispatch.
// Used by RescheduleAppointmentCommandHandler which needs the single-method interface.
builder.Services.AddScoped<IAppointmentConfirmationEmailService>(sp =>
{
    var pdfService = sp.GetRequiredService<IPdfConfirmationService>();
    var emailService = sp.GetRequiredService<IEmailService>();
    var logger = sp.GetRequiredService<ILogger<AppointmentConfirmationEmailServiceAdapter>>();
    return new AppointmentConfirmationEmailServiceAdapter(pdfService, emailService, logger);
});
Log.Information("AppointmentConfirmationEmailService registered (US_020/US_021, task_003).");

// ── US_021 — Booking confirmation notification pipeline (task_002) ─────────────
// Channel<ConfirmationRetryRequest>: in-process unbounded queue for retry orchestration.
// Registered as Singleton so both BookingConfirmedEventHandler and
// PdfConfirmationRetryService share the same channel instance.
builder.Services.AddSingleton(Channel.CreateUnbounded<ConfirmationRetryRequest>(
    new UnboundedChannelOptions { SingleReader = true }));

// PdfConfirmationRetryService: long-running BackgroundService draining the retry channel.
builder.Services.AddHostedService<PdfConfirmationRetryService>();

// ── US_025 — SMS retry background job (task_002, AC-3) ───────────────────────
// SmsRetryRepository: EF Core query repository for retry-eligible SMS Notification records.
// Registered as Scoped so it is resolved per poll cycle via IServiceScopeFactory inside
// SmsRetryBackgroundService (avoids captive-dependency issue in .NET hosted services).
builder.Services.AddScoped<SmsRetryRepository>();
// SmsRetryBackgroundService: polls every 2 min for failed SlotSwapNotification SMS records
// with retryCount = 0 and sentAt <= UtcNow - 5 min, then dispatches NotificationRetryCommand.
builder.Services.AddHostedService<SmsRetryBackgroundService>();

// ── US_033 — Reminder scheduler background service (task_001) ─────────────────
// ISystemSettingsRepository: reads configurable reminder intervals from system_settings table.
builder.Services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
// IAppointmentReminderRepository: queries Booked/Cancelled appointments for reminder evaluation.
// IgnoreQueryFilters() is used internally to bypass the Cancelled soft-delete filter (AC-4).
builder.Services.AddScoped<IAppointmentReminderRepository, AppointmentReminderRepository>();

// ── US_033 — Notification dispatch layer (task_002) ───────────────────────────
// IEmailNotifier: SendGrid implementation that composes and delivers reminder emails (AC-2).
builder.Services.AddTransient<IEmailNotifier, SendGridEmailNotifier>();
// ISmsNotifier: Twilio implementation that composes and delivers reminder SMS (AC-2, Edge Case 1).
builder.Services.AddTransient<ISmsNotifier, TwilioSmsNotifier>();
// INotificationDispatcher: orchestrates per-channel dispatch, outcome persistence, and audit logging.
// Scoped lifetime so it shares the IServiceScope created per tick by ReminderSchedulerService.
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatchService>();
Log.Information("NotificationDispatchService registered (US_033, task_002).");

// ReminderSchedulerService: 5-minute PeriodicTimer loop that creates Pending Notification records
// for each unprocessed reminder window (48h, 24h, 2h) and suppresses reminders for Cancelled
// appointments. Resumes overdue Pending jobs on startup (at-least-once delivery, Edge Case 2).
builder.Services.AddHostedService<ReminderSchedulerService>();
Log.Information("ReminderSchedulerService registered (US_033, task_001).");

// ── EP-011/US_052 — Booking notification fire-and-try dispatch + retry (task_002) ──
// INotificationDispatchService: fire-and-try service called by booking/reminder command handlers.
// Persists a Pending Notification BEFORE delivery attempt; on failure returns Queued instead of
// throwing, so the booking workflow is never blocked (NFR-018, AC-2).
builder.Services.AddScoped<INotificationDispatchService, BookingNotificationDispatchService>();
// NotificationRetryBackgroundService: 60-second loop that retries Pending booking-confirmation
// notifications with exponential backoff (4^retryCount minutes, max 3 attempts).
// After 3 failed attempts, sets Status = Failed and emits a Serilog Warning (US_052, AC-2).
builder.Services.AddHostedService<NotificationRetryBackgroundService>();
Log.Information("NotificationRetryBackgroundService registered (US_052, task_002).");

// ── EP-005/US_028 — AI Intake session store + service (task_002 + task_003) ───
// IntakeSessionStore: singleton ConcurrentDictionary<Guid, IntakeSession> with a background
// timer that evicts sessions idle for more than 60 minutes (prevents memory leak, US_028).
builder.Services.AddSingleton<Propel.Modules.AI.Services.IntakeSessionStore>();

// ── Bind AiSettings from "Ai" section (AIR-O01, AIR-O02, AIR-O03) ────────────
// API keys are NEVER stored in appsettings — read from env vars only (OWASP A02).
builder.Services.Configure<AiSettings>(configuration.GetSection("Ai"));
var aiSettings = configuration.GetSection("Ai").Get<AiSettings>() ?? new AiSettings();

// ── Register Semantic Kernel chat completion service ──────────────────────────
// Dev/staging: direct OpenAI endpoint. Production: Azure OpenAI (HIPAA BAA path).
if (aiSettings.UseAzureOpenAI)
{
    var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? throw new InvalidOperationException(
            "AZURE_OPENAI_ENDPOINT environment variable is required when Ai:UseAzureOpenAI = true.");
    var azureApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? throw new InvalidOperationException(
            "AZURE_OPENAI_API_KEY environment variable is required when Ai:UseAzureOpenAI = true.");

    builder.Services.AddAzureOpenAIChatCompletion(
        deploymentName : aiSettings.ModelDeploymentName,
        endpoint       : azureEndpoint,
        apiKey         : azureApiKey);

    Log.Information("AI: Azure OpenAI chat completion registered (deployment={Deployment})", aiSettings.ModelDeploymentName);
}
else
{
    var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException(
            "OPENAI_API_KEY environment variable is required when Ai:UseAzureOpenAI = false.");

    builder.Services.AddOpenAIChatCompletion(
        modelId : aiSettings.ModelDeploymentName,
        apiKey  : openAiApiKey);

    Log.Information("AI: OpenAI chat completion registered (model={Model})", aiSettings.ModelDeploymentName);
}

// ── Polly circuit breaker pipeline (AIR-O02) ─────────────────────────────────
// Opens after CircuitBreakerFailureThreshold (3) consecutive failures within
// CircuitBreakerWindowSeconds (300 s = 5 min). Break duration: 60 s.
// Registered as singleton — the same circuit state is shared across all scoped requests.
var aiCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio       = 1.0,
        MinimumThroughput  = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration   = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration      = TimeSpan.FromSeconds(60),
        ShouldHandle       = new PredicateBuilder().Handle<Exception>(),
        OnOpened           = _ => { Log.Warning("AiCircuitBreaker_Opened"); return ValueTask.CompletedTask; },
        OnClosed           = _ => { Log.Information("AiCircuitBreaker_Closed"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddSingleton(aiCircuitBreaker);

// ── Polly circuit breaker for risk augmenter (us_031, task_003, AIR-O02) ─────
// Separate keyed pipeline — prevents US_028 intake circuit from tripping the risk augmenter circuit.
// 3 consecutive failures / 5-min window → BrokenCircuitException → AiNoShowRiskUnavailableException.
var riskAugmenterCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio      = 1.0,
        MinimumThroughput = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration  = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration     = TimeSpan.FromSeconds(60),
        ShouldHandle      = new PredicateBuilder().Handle<Exception>(),
        OnOpened          = _ => { Log.Warning("AiCircuitBreaker_Opened_RiskAugmenter"); return ValueTask.CompletedTask; },
        OnClosed          = _ => { Log.Information("AiCircuitBreaker_Closed_RiskAugmenter"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddKeyedSingleton<ResiliencePipeline>("risk-augmenter", riskAugmenterCircuitBreaker);

// ── AI intake helpers and concrete service (task_003) ────────────────────────
builder.Services.AddSingleton<IntakePromptBuilder>();
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiIntakeService,
    Propel.Modules.AI.Services.SemanticKernelAiIntakeService>();

// ── US_040 — AI RAG vector store: pgvector chunk storage and retrieval (task_002) ──────────
// TEMPORARY: Vector store disabled until pgvector extension is installed
// IDocumentChunkEmbeddingRepository: EF Core + raw SQL pgvector <=> cosine similarity search.
// ACL filter (AIR-S02), threshold filtering (AIR-R02), and re-ranking (AIR-R03) applied per retrieval.
// builder.Services.AddScoped<IDocumentChunkEmbeddingRepository, DocumentChunkEmbeddingRepository>();
// IVectorStoreService: orchestrates StoreChunksAsync (task_001→task_002 handoff) and
// RetrieveRelevantChunksAsync (task_002→task_003 handoff) with full AIR pipeline enforcement.
// builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IVectorStoreService,
//     Propel.Modules.AI.Services.VectorStoreService>();
// Log.Information("VectorStoreService registered (US_040, task_002).");

// ── US_040 — AI RAG extraction orchestrator (task_003) ─────────────────────────────────────
// Polly circuit breaker for the extraction pipeline — isolated from the intake and risk circuits.
// Opens after 3 consecutive failures within 5 minutes; resets after a 60-second break (AIR-O02).
var extractionCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio      = 1.0,
        MinimumThroughput = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration  = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration     = TimeSpan.FromSeconds(60),
        ShouldHandle      = new PredicateBuilder().Handle<Exception>(),
        OnOpened          = _ => { Log.Warning("AiCircuitBreaker_Opened_Extraction"); return ValueTask.CompletedTask; },
        OnClosed          = _ => { Log.Information("AiCircuitBreaker_Closed_Extraction"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddKeyedSingleton<ResiliencePipeline>("extraction", extractionCircuitBreaker);

// ExtractionGuardrailFilter: singleton schema + content safety validator (AIR-Q03, AIR-S04).
builder.Services.AddSingleton<Propel.Modules.AI.Guardrails.ExtractionGuardrailFilter>();

// IExtractedDataRepository: EF Core INSERT-only repository for AI-extracted fields (AC-3, AIR-001).
builder.Services.AddScoped<IExtractedDataRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.ExtractedDataRepository>();

// ── EP-008-I/us_041 — 360-degree aggregation API repositories (task_002) ──────
// IDataConflictRepository: parameterised LINQ query for unresolved Critical conflicts (AC-4).
builder.Services.AddScoped<IDataConflictRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.DataConflictRepository>();
// IPatientProfileVerificationRepository: upsert verification record per patient (AC-3).
builder.Services.AddScoped<IPatientProfileVerificationRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.PatientProfileVerificationRepository>();
Log.Information("360-view aggregation repositories registered (EP-008-I/us_041, task_002).");

// IExtractionOrchestrator: full RAG + GPT-4o extraction pass per document (US_040, AC-2, AC-3, AIR-O01, AIR-O02).
        // TEMPORARY: ExtractionOrchestrator disabled until pgvector extension is installed
        // builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IExtractionOrchestrator,
        //     Propel.Modules.AI.Services.ExtractionOrchestrator>();
        // Log.Information("ExtractionOrchestrator registered (US_040, task_003).");

// ── EP-008-I/us_041 — AI semantic de-duplication service (task_003) ───────────
// Isolated Polly circuit breaker: 3 consecutive GPT-4o failures / 5-min window → FallbackManual
// path (AIR-O02). Keyed separately from extraction and intake circuits to prevent cross-tripping.
var deduplicationCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio      = 1.0,
        MinimumThroughput = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration  = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration     = TimeSpan.FromSeconds(60),
        ShouldHandle      = new PredicateBuilder().Handle<Exception>(),
        OnOpened          = _ => { Log.Warning("AiCircuitBreaker_Opened_Deduplication"); return ValueTask.CompletedTask; },
        OnClosed          = _ => { Log.Information("AiCircuitBreaker_Closed_Deduplication"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddKeyedSingleton<ResiliencePipeline>("deduplication", deduplicationCircuitBreaker);

// IPatientDeduplicationService: pgvector cosine similarity + GPT-4o confirmation + canonical
// selection per patient. Scoped lifetime shares DbContext with request-level repositories.
// AIR-S01 (PII redaction), AIR-S03 (audit log), AIR-O01 (token budget), AIR-O02 (circuit breaker).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IPatientDeduplicationService,
    Propel.Modules.AI.Services.PatientDeduplicationService>();
Log.Information("PatientDeduplicationService registered (EP-008-I/us_041, task_003).");

// ── EP-008-II/us_042 — AI Medical Coding Suggestion Pipeline (task_001) ───────
// Isolated Polly circuit breaker: 3 consecutive GPT-4o failures / 5-min window → open for 60s.
// Keyed "medical-coding" to prevent cross-tripping with extraction, intake, and deduplication circuits.
var medicalCodingCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio      = 1.0,
        MinimumThroughput = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration  = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration     = TimeSpan.FromSeconds(60),
        ShouldHandle      = new PredicateBuilder().Handle<Exception>(),
        OnOpened          = _ => { Log.Warning("AiCircuitBreaker_Opened_MedicalCoding"); return ValueTask.CompletedTask; },
        OnClosed          = _ => { Log.Information("AiCircuitBreaker_Closed_MedicalCoding"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddKeyedSingleton<ResiliencePipeline>("medical-coding", medicalCodingCircuitBreaker);

// MedicalCodingPlugin: Semantic Kernel plugin with [KernelFunction] ICD-10 and CPT tool methods.
// Singleton because it holds no mutable state — only reads prompt files from disk (AIR-O03).
builder.Services.AddSingleton<Propel.Modules.AI.Services.MedicalCodingPlugin>();

// MedicalCodeSchemaValidator: singleton schema + anti-hallucination validator (AIR-Q03, AC-3).
builder.Services.AddSingleton<Propel.Modules.AI.Validators.MedicalCodeSchemaValidator>();

// ICodeReferenceLibrary: singleton in-memory ICD-10/CPT reference validator shared between the
// AI anti-hallucination pipeline and the Staff code-confirmation API (EP-008-II/us_043, task_002, AC-4).
builder.Services.AddSingleton<Propel.Domain.Interfaces.ICodeReferenceLibrary,
    Propel.Modules.AI.Services.CodeReferenceLibrary>();

// IMedicalCodeRepository: EF Core scoped repository for accept/reject upserts and manual inserts
// in the Staff confirmation workflow (EP-008-II/us_043, task_002, AC-2, AC-3, AC-4).
builder.Services.AddScoped<Propel.Domain.Interfaces.IMedicalCodeRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.MedicalCodeRepository>();

// IMedicalCodingOrchestrator: sequential ICD-10 → CPT pipeline with circuit breaker, schema validation,
// low-confidence flagging, and audit logging. Scoped to share DbContext with request repositories.
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IMedicalCodingOrchestrator,
    Propel.Modules.AI.Services.MedicalCodingOrchestrator>();
Log.Information("MedicalCodingOrchestrator registered (EP-008-II/us_042, task_001).");

// ── EP-008-II/us_044 — AI Conflict Detection Pipeline (task_001) ──────────────
// Isolated Polly circuit breaker: 3 consecutive GPT-4o failures / 5-min window → open for 60s.
// Keyed "conflict-detection" to prevent cross-tripping with extraction, deduplication, and medical-coding circuits.
var conflictDetectionCircuitBreaker = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio      = 1.0,
        MinimumThroughput = aiSettings.CircuitBreakerFailureThreshold,
        SamplingDuration  = TimeSpan.FromSeconds(aiSettings.CircuitBreakerWindowSeconds),
        BreakDuration     = TimeSpan.FromSeconds(60),
        ShouldHandle      = new PredicateBuilder().Handle<Exception>(),
        OnOpened          = _ => { Log.Warning("AiCircuitBreaker_Opened_ConflictDetection"); return ValueTask.CompletedTask; },
        OnClosed          = _ => { Log.Information("AiCircuitBreaker_Closed_ConflictDetection"); return ValueTask.CompletedTask; }
    })
    .Build();
builder.Services.AddKeyedSingleton<ResiliencePipeline>("conflict-detection", conflictDetectionCircuitBreaker);

// ConflictDetectionPlugin: Semantic Kernel plugin with [KernelFunction] DetectConflictsAsync method.
// Singleton because it holds no mutable state — only reads prompt files from disk (AIR-O03).
builder.Services.AddSingleton<Propel.Modules.AI.Services.ConflictDetectionPlugin>();

// ConflictDetectionSchemaValidator: singleton schema validator for AI output (AIR-Q03).
builder.Services.AddSingleton<Propel.Modules.AI.Validators.ConflictDetectionSchemaValidator>();

// IConflictDetectionOrchestrator: RAG conflict detection pipeline with circuit breaker,
// schema validation, severity classification, and idempotent persistence (EP-008-II/us_044, task_001).
// Scoped to share DbContext with request-level repositories.
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IConflictDetectionOrchestrator,
    Propel.Modules.AI.Services.ConflictDetectionOrchestrator>();
Log.Information("ConflictDetectionOrchestrator registered (EP-008-II/us_044, task_001).");

// ── EP-010/us_048 — AI Quality Monitoring & Guardrails (task_001 / task_002) ──────────────
// IAiMetricsWriter: EF Core INSERT-only implementation (task_002 — EfAiMetricsWriter).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiMetricsWriter,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiMetricsWriter>();

// IAiMetricsReadRepository: EF Core rolling-window read implementation (task_002 — EfAiMetricsReadRepository).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiMetricsReadRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiMetricsReadRepository>();

// AgreementRateEvaluator: computes rolling AI-Human Agreement Rate; warns + flags Redis if < 98% (AIR-Q01).
// Redis is injected as null in development mode (graceful degradation, NFR-018).
builder.Services.AddScoped<Propel.Modules.AI.Metrics.AgreementRateEvaluator>(sp =>
{
    var metricsRepo = sp.GetRequiredService<Propel.Modules.AI.Interfaces.IAiMetricsReadRepository>();
    IConnectionMultiplexer? redis = null;
    if (!builder.Environment.IsDevelopment())
    {
        try { redis = sp.GetRequiredService<IConnectionMultiplexer>(); }
        catch { /* Redis unavailable — degrade gracefully */ }
    }
    return new Propel.Modules.AI.Metrics.AgreementRateEvaluator(metricsRepo, redis);
});

// HallucinationRateEvaluator: computes rolling hallucination rate; raises Fatal alert + Redis flag if > 2% (AIR-Q04).
builder.Services.AddScoped<Propel.Modules.AI.Metrics.HallucinationRateEvaluator>(sp =>
{
    var metricsRepo = sp.GetRequiredService<Propel.Modules.AI.Interfaces.IAiMetricsReadRepository>();
    IConnectionMultiplexer? redis = null;
    if (!builder.Environment.IsDevelopment())
    {
        try { redis = sp.GetRequiredService<IConnectionMultiplexer>(); }
        catch { /* Redis unavailable — degrade gracefully */ }
    }
    return new Propel.Modules.AI.Metrics.HallucinationRateEvaluator(metricsRepo, redis);
});

// IAiAgreementEventEmitter: maps staff decisions to agreement events; triggers AgreementRateEvaluator (AIR-Q01).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiAgreementEventEmitter,
    Propel.Modules.AI.Services.AiAgreementEventEmitter>();

// AiOutputSchemaValidator: SK IFunctionInvocationFilter — validates JSON output schema; rejects invalid outputs (AIR-Q03).
// Changed from Singleton to Scoped to fix lifetime mismatch with scoped IAiMetricsWriter (EP-010/us_048).
builder.Services.AddScoped<Propel.Modules.AI.Guardrails.AiOutputSchemaValidator>();
Log.Information("AI quality monitoring services registered (EP-010/us_048, task_001).");

// ── EP-010/us_049 — AI Safety Guardrails & Immutable Prompt Audit Logging (task_001) ─────────────
// IContentSafetyEvaluator: Phase 1 keyword blocklist from AiSafety:BlockedKeywords (AIR-S04, AC-3).
builder.Services.AddSingleton<Propel.Modules.AI.Guardrails.IContentSafetyEvaluator,
    Propel.Modules.AI.Guardrails.KeywordContentSafetyEvaluator>();

// PiiRedactionFilter: SK IPromptRenderFilter — replaces 6 PII categories before OpenAI transmission (AIR-S01, AC-1).
// Registered as singleton — stateless; uses compiled Regex patterns.
builder.Services.AddSingleton<Propel.Modules.AI.Guardrails.PiiRedactionFilter>();

// ContentSafetyFilter: SK IFunctionInvocationFilter — keyword blocklist + IContentSafetyEvaluator post-response (AIR-S04, AC-3).
builder.Services.AddSingleton<Propel.Modules.AI.Guardrails.ContentSafetyFilter>();

// AiPromptAuditHook: SK IFunctionInvocationFilter (last) — try/finally capture; IAiPromptAuditWriter (AIR-S03, AC-4).
// Registered as scoped because IAiPromptAuditWriter (task_002 EfAiPromptAuditWriter) is scoped.
builder.Services.AddScoped<Propel.Modules.AI.Guardrails.AiPromptAuditHook>();

// RagAclFilter: chunk-level ACL predicate injected directly into RAG orchestrators (AIR-S02, AC-2).
builder.Services.AddScoped<Propel.Modules.AI.Guardrails.RagAclFilter>();

// IAiPromptAuditWriter: EfAiPromptAuditWriter — INSERT-only EF Core; swallows exceptions (AIR-S03, AC-4, task_002).
// Replaces NullAiPromptAuditWriter stub registered by task_001.
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiPromptAuditWriter,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiPromptAuditWriter>();

// IAiPromptAuditReadRepository: EfAiPromptAuditReadRepository — keyset-paginated read for Admin query (AC-4, task_002).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiPromptAuditReadRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiPromptAuditReadRepository>();
Log.Information("AI safety guardrails registered (EP-010/us_049, task_001+task_002): PiiRedaction, ContentSafety, AuditHook, RagAcl, EfAuditWriter, AuditReadRepo.");

// ── EP-010/us_050 — AI Operational Resilience: Circuit Breaker, Token Budget & Model Swap ─────
builder.Services.AddAiOperationalResilience(configuration);

// IAiOperationalMetricsWriter: EF Core INSERT-only; swallows exceptions (fire-and-forget contract, NFR-018).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiOperationalMetricsWriter,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiOperationalMetricsWriter>();

// IAiOperationalMetricsReadRepository: keyset-style rolling-window reads for operational dashboard (AIR-O04, AC-4).
builder.Services.AddScoped<Propel.Modules.AI.Interfaces.IAiOperationalMetricsReadRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.EfAiOperationalMetricsReadRepository>();

Log.Information("AI operational metrics services registered (EP-010/us_050, task_002): EfOperationalMetricsWriter, EfOperationalMetricsReadRepo.");

// IClinicalDocumentRepository: EF Core repository for Pending document polling and status transitions.
builder.Services.AddScoped<IClinicalDocumentRepository,
    Propel.Api.Gateway.Infrastructure.Repositories.ClinicalDocumentRepository>();
    
// TEMPORARY: ExtractionPipelineWorker disabled until pgvector extension is installed
// ExtractionPipelineWorker: 30-second PeriodicTimer worker orchestrating the full extraction
// pipeline (ChunkAsync → GenerateAsync → StoreChunksAsync → ExtractAsync) with SemaphoreSlim(3)
// concurrency control and idempotency locking via ProcessingStatus = Processing (AC-1, AC-4, EC-1, EC-2).
/* builder.Services.AddHostedService<Propel.Modules.Clinical.Workers.ExtractionPipelineWorker>();
Log.Information("ExtractionPipelineWorker registered (US_040, task_004)."); */

// ── us_031 — No-Show Risk Engine (task_002 + task_003) ────────────────────────
// INoShowRiskCalculator: scoped rule-based engine with AI augmentation hook.
builder.Services.AddScoped<INoShowRiskCalculator, RuleBasedNoShowRiskCalculator>();
// IAiNoShowRiskAugmenter: concrete SK implementation (task_003 — replaces NullAiNoShowRiskAugmenter stub).
builder.Services.AddSingleton<Propel.Modules.Risk.Services.RiskAssessmentPromptBuilder>();
builder.Services.AddScoped<IAiNoShowRiskAugmenter,
    Propel.Modules.Risk.Services.SemanticKernelNoShowRiskAugmenter>();
// INoShowRiskRepository: EF Core repository for risk data queries and UPSERT.
builder.Services.AddScoped<INoShowRiskRepository, Propel.Api.Gateway.Infrastructure.Repositories.NoShowRiskRepository>();
// NoShowRiskCalculationBackgroundService: 1-hour periodic batch job.
builder.Services.AddHostedService<Propel.Modules.Risk.BackgroundServices.NoShowRiskCalculationBackgroundService>();
Log.Information("NoShowRiskCalculationBackgroundService registered (us_031, task_002).");

// ── EP-007/us_035 — Google Calendar OAuth 2.0 sync (task_002) ─────────────────
// GoogleCalendarSettings: non-secret OAuth settings bound from appsettings.json (OWASP A02).
// GOOGLE_CLIENT_SECRET is read exclusively from Environment.GetEnvironmentVariable at runtime.
builder.Services.Configure<GoogleCalendarSettings>(configuration.GetSection("GoogleCalendar"));

// IGoogleCalendarService: wraps Google.Apis.Calendar.v3; handles event create/update, token refresh.
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();

// IcsGenerationService: Ical.Net wrapper; RFC 5545 ICS file generation (AC-4 fallback).
builder.Services.AddScoped<IcsGenerationService>();

// ICalendarSyncRepository and IPatientOAuthTokenRepository: EF Core repositories for upsert/read.
builder.Services.AddScoped<ICalendarSyncRepository, Propel.Api.Gateway.Infrastructure.Repositories.CalendarSyncRepository>();
builder.Services.AddScoped<IPatientOAuthTokenRepository, Propel.Api.Gateway.Infrastructure.Repositories.PatientOAuthTokenRepository>();

// IOAuthStateService: PKCE state store — InMemory in development, Redis in production (OWASP A07).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IOAuthStateService, InMemoryOAuthStateService>();
    Log.Information("OAuthStateService: Using IN-MEMORY store (development mode).");
}
else
{
    builder.Services.AddScoped<IOAuthStateService, RedisOAuthStateService>();
    Log.Information("OAuthStateService: Using Redis store (production mode).");
}

// AddHttpClient: required by GoogleCalendarService for token refresh and by HandleGoogleCallbackCommandHandler.
builder.Services.AddHttpClient();

// ── EP-007/us_037 — Calendar Propagation Service (task_001) ───────────────────
// IOAuthTokenService: get + silent-refresh OAuth tokens for Google/Outlook (EC-1, OWASP A02).
builder.Services.AddScoped<IOAuthTokenService, OAuthTokenService>();
// IGoogleCalendarAdapter: Google Calendar API v3 PATCH/DELETE adapter (AC-1, AC-2).
builder.Services.AddScoped<IGoogleCalendarAdapter, GoogleCalendarAdapter>();
// IOutlookCalendarAdapter: Microsoft Graph API v1.0 PATCH/DELETE adapter (AC-1, AC-2).
builder.Services.AddScoped<IOutlookCalendarAdapter, OutlookCalendarAdapter>();
// ICalendarPropagationService: orchestrates provider routing, token refresh, and status updates (us_037).
builder.Services.AddScoped<Propel.Modules.Appointment.Infrastructure.ICalendarPropagationService, CalendarPropagationService>();
Log.Information("CalendarPropagationService registered (EP-007, us_037, task_001).");

// CalendarSyncRetryBackgroundService: 5-min PeriodicTimer; retries Failed CalendarSync records (AC-4).
builder.Services.AddHostedService<CalendarSyncRetryBackgroundService>();
Log.Information("CalendarSyncRetryBackgroundService registered (EP-007, us_035, task_002).");

// CalendarSyncRetryProcessor: 5-min PeriodicTimer; retries Failed CalendarSync records for both
// Google and Outlook via ICalendarPropagationService with SemaphoreSlim(5) rate limiting (US_037, AC-3, EC-2).
builder.Services.AddHostedService<CalendarSyncRetryProcessor>();
Log.Information("CalendarSyncRetryProcessor registered (EP-007, us_037, task_002).");

// ── EP-007/us_036 — Outlook Calendar OAuth 2.0 sync (task_002) ────────────────
// OutlookCalendarOptions: non-secret settings bound from appsettings.json (OWASP A02).
// OUTLOOK_CLIENT_SECRET is sourced exclusively from Key Vault / environment variables.
builder.Services.Configure<OutlookCalendarOptions>(configuration.GetSection("OutlookCalendar"));

// IIcsGeneratorService: RFC 5545-compliant ICS generator shared with us_035 Google flow.
builder.Services.AddScoped<IIcsGeneratorService, IcsGeneratorService>();

// ── EP-011/us_052 — Calendar Sync Degradation Handlers (task_003) ─────────────
// ICalendarSyncService (keyed): booking-time sync with graceful degradation (NFR-018, AC-4).
// GoogleCalendarSyncService persists CalendarSync.syncStatus=Failed on API exception;
// MicrosoftGraphCalendarSyncService follows the same pattern for Outlook.
builder.Services.AddKeyedScoped<ICalendarSyncService, GoogleCalendarSyncService>("Google");
builder.Services.AddKeyedScoped<ICalendarSyncService, MicrosoftGraphCalendarSyncService>("Outlook");
Log.Information("ICalendarSyncService registered (EP-011, us_052, task_003): Google + Outlook keyed implementations.");

// Channel<OutlookRetryRequest>: in-process unbounded channel for Outlook retry orchestration.
// Singleton so both HandleOutlookCallbackCommandHandler and OutlookCalendarRetryService
// share the same channel instance.
builder.Services.AddSingleton(System.Threading.Channels.Channel.CreateUnbounded<OutlookRetryRequest>(
    new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true }));

// OutlookCalendarRetryService: BackgroundService consuming Channel<OutlookRetryRequest>;
// waits 10 minutes then retries once; LogWarning on second failure; never throws (AG-6, AC-4).
builder.Services.AddHostedService<OutlookCalendarRetryService>();
Log.Information("OutlookCalendarRetryService registered (EP-007, us_036, task_002).");

// ═══════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ═══════════════════════════════════════════════════════════════════════════════

// ── Middleware pipeline order (AC3) ──────────────────────────────────────────
// 1. Correlation ID must be first to propagate to all downstream middleware/handlers (AC-2, TR-018)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Request logging — must follow CorrelationIdMiddleware so CorrelationId is in HttpContext.Items (AC-1)
app.UseMiddleware<RequestLoggingMiddleware>();

// 3. Global exception handler — must be early to catch all downstream exceptions (NFR-014)
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

// HSTS — Strict-Transport-Security header on all HTTPS responses (NFR-005, AC-4)
// Must follow UseHttpsRedirection so it only fires on HTTPS responses.
app.UseHsts();

// 2. Rate limiting before auth/routing
app.UseRateLimiter();

// 3. CORS before authentication so preflight OPTIONS requests are handled correctly
app.UseCors("NetlifyPolicy");

// 4. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 5. Session alive check — must run after UseAuthentication so User.Claims are populated (US_011, AC-2)
app.UseMiddleware<SessionAliveMiddleware>();

// 6. RBAC stub after authentication so User.Claims are populated
app.UseMiddleware<RbacMiddleware>();

// ── Swagger UI (all environments for scaffolding; restrict in production later) ─
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Propel IQ API v1");
    c.RoutePrefix = "swagger";
});

app.MapControllers().RequireRateLimiting("global");

// ── /health — full platform health report: all 8 checks (EP-011/us_052, AC-1, NFR-003) ────────
// Degraded maps to HTTP 200 — partial availability is not fatal (NFR-018, AG-6).
// Unhealthy (PostgreSQL only) maps to HTTP 503 — signals load balancers and uptime monitors.
// No authentication required; structured JSON via HealthCheckResponseWriter (no credentials exposed).
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

// ── /health/live — liveness probe: DB only (container orchestrators / Railway health-gate) ──
// Includes only checks tagged "critical" (postgresql + pgcrypto).
// Returns 200 when DB is reachable, 503 when DB is unreachable (AC-1 edge-case).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate      = check => check.Tags.Contains("critical"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    }
});

// ── /healthz — detailed health check: DB + Redis status (Docker Compose / internal monitoring) ─
HealthCheckEndpoint.MapHealthCheck(app);

// ── Startup: apply EF Core migrations then seed reference data (AC2) ─────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("[Startup] Migrations applied successfully.");
    
    // Seed all master/reference data (idempotent)
    await SeedData.SeedAllMasterDataAsync(db);
}

app.Run();
