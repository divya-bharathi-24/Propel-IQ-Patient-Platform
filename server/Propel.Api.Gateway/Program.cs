using Npgsql;
// TODO: Uncomment when pgvector is installed and AI features are ready
// using Pgvector.EntityFrameworkCore;
// using Pgvector.Npgsql;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Propel.Api.Gateway.Data;
using Propel.Api.Gateway.Endpoints;
using Propel.Api.Gateway.Infrastructure.BackgroundServices;
using Propel.Api.Gateway.Infrastructure.Email;
using Propel.Api.Gateway.Infrastructure.Filters;
using Propel.Api.Gateway.Infrastructure.Repositories;
using Propel.Api.Gateway.Infrastructure.Security;
using Propel.Api.Gateway.Middleware;
using Propel.Api.Gateway.Security;
using Propel.Domain.Interfaces;
using Propel.Modules.Auth.Audit;
using Propel.Modules.Auth.Services;
using Serilog;
using StackExchange.Redis;

// ── Module assembly references ────────────────────────────────────────────────
using Propel.Modules.Admin.Commands;
using Propel.Modules.AI.Commands;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Auth.Commands;
using Propel.Modules.Clinical.Commands;
using Propel.Modules.Notification.Commands;
using Propel.Modules.Patient.Commands;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ── Kestrel TLS 1.2+ enforcement (NFR-005, AG-2, AC-4) ───────────────────────
// Restricts Kestrel to TLS 1.2 and TLS 1.3 for direct HTTPS scenarios (local dev).
// On Railway, TLS is terminated at the platform ingress; this setting adds defence-in-depth.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
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

// ── MediatR (all 7 module assemblies + gateway) ───────────────────────────────
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
    typeof(PingAuthCommand).Assembly,
    typeof(PingPatientCommand).Assembly,
    typeof(PingAppointmentCommand).Assembly,
    typeof(PingClinicalCommand).Assembly,
    typeof(PingAICommand).Assembly,
    typeof(PingNotificationCommand).Assembly,
    typeof(PingAdminCommand).Assembly
));

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
    typeof(PingAdminCommand).Assembly
]);

// ── Entity Framework Core 9 + PostgreSQL ─────────────────────────────────────
// DATABASE_URL env var (Railway standard) takes precedence over appsettings ConnectionStrings.
var connectionString = configuration["DATABASE_URL"]
    ?? configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DATABASE_URL is required.");

// TODO: Uncomment when pgvector is installed and AI features are ready
// UseVector() registers the pgvector type handler so the Npgsql driver can
// serialize/deserialize float[] ↔ vector columns at runtime (task_002, AC-2).
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
// dataSourceBuilder.UseVector();  // COMMENTED OUT - AI features disabled temporarily
var dataSource = dataSourceBuilder.Build();

// IDbContextFactory<AppDbContext> — used by AuditLogRepository to create isolated DbContext
// scopes per audit write, preventing audit entries from being rolled back by outer business
// transactions (US_013, AD-7). Also provides pooled DbContext instances for request-scoped
// repositories. Configured with Singleton options lifetime to prevent scoped service consumption
// from singleton factory (EF Core DI pattern).
builder.Services.AddDbContextFactory<AppDbContext>(
    (serviceProvider, opt) =>
    {
        // TODO: Uncomment when pgvector is installed and AI features are ready
        // UseVector() registers EF Core type mappings for Pgvector.Vector ↔ vector(N) columns (task_003, AC-2).
        opt.UseNpgsql(dataSource /*, o => o.UseVector() */)  // COMMENTED OUT - AI features disabled temporarily
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
// abortConnect=false: the app starts even when Redis is unreachable.
// All Redis read/write paths must catch RedisConnectionException and append
// X-Degraded: redis to the response (see controllers/endpoints using IDatabase).
// REDIS_URL env var (Railway/Upstash standard) takes precedence over appsettings.
var redisUseLocal = configuration.GetValue<bool>("Redis:UseLocal");
var rawRedisConn = redisUseLocal
    ? (configuration["Redis:LocalConnectionString"] ?? "redis:6379")
    : (configuration["REDIS_URL"] ?? configuration["Redis:ConnectionString"] ?? "redis:6379");

var redisOptions = ConfigurationOptions.Parse(rawRedisConn);
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectRetry = 3;
redisOptions.ReconnectRetryPolicy = new LinearRetry(2_000);

builder.Services.AddSingleton<IConnectionMultiplexer>(

    ConnectionMultiplexer.Connect(redisOptions));

// ── ASP.NET Core Data Protection — AES-256 key ring for PHI encryption (NFR-004, NFR-013) ───
// Key lifetime: 90-day rotation. All historical keys are retained so previously
// encrypted DB values can always be decrypted after rotation.
// Local dev: keys persist to file system. Production: Upstash Redis key store.
var dpBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("propeliq-platform")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

if (!builder.Environment.IsDevelopment())
{
    // Production: persist keys to Upstash Redis (IConnectionMultiplexer already created above)
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
    dpBuilder.PersistKeysToStackExchangeRedis(redisMultiplexer, "DataProtection-Keys");
}
else
{
    // Local dev: persist keys to a configurable file system path (never ephemeral in-memory)
    var keyPath = configuration["DataProtection:KeyPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys");
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

// ── Argon2id password hasher (NFR-008, DRY) — replaces direct Argon2 calls in handlers ──
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

// ── PHI encryption service (NFR-004, NFR-013) — AES-256 via Data Protection key ring ──
builder.Services.AddSingleton<IPhiEncryptionService, AesGcmPhiEncryptionService>();

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

// ── ASP.NET Core Health Checks — maps /health liveness probe (Railway health-gate + AC-2) ───
// No authentication required; /health is a liveness probe, not a data endpoint.
// PgcryptoHealthCheck verifies the pgcrypto extension is active before traffic is accepted (task_002, AC-4).
builder.Services.AddHealthChecks()
    .AddCheck<PgcryptoHealthCheck>("pgcrypto");

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

// ── Middleware singletons ─────────────────────────────────────────────────────
builder.Services.AddTransient<CorrelationIdMiddleware>();
builder.Services.AddTransient<RbacMiddleware>();
builder.Services.AddTransient<ExceptionHandlingMiddleware>();
builder.Services.AddTransient<SessionAliveMiddleware>();

// ── Registration / Verification repositories (us_010 task_002) ───────────────
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// ── US_013 — Audit logging service (task_001): retry-once wrapper + Critical alert ──
// AuditLogService is scoped so it shares the DI scope with request handlers.
// SessionExpirySubscriberService creates its own scope via IServiceScopeFactory.
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddHttpContextAccessor();

// ── US_013 — Session expiry background service (task_001) ─────────────────────
builder.Services.AddHostedService<SessionExpirySubscriberService>();

// ── US_011 — Auth services and refresh-token repository (task_002) ────────────
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IRedisSessionService, RedisSessionService>();

// ── US_012 — Admin account management repositories (task_002) ─────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICredentialSetupTokenRepository, CredentialSetupTokenRepository>();

// ── Email service — SendGrid (us_010 task_002, NFR-018) ──────────────────────
builder.Services.AddTransient<IEmailService, SendGridEmailService>();

// ═══════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ═══════════════════════════════════════════════════════════════════════════════

// ── Middleware pipeline order (AC3) ──────────────────────────────────────────
// 1. Correlation ID must be first to propagate to all downstream middleware/handlers
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Global exception handler — must be early to catch all downstream exceptions (NFR-014)
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

// ── /health — ASP.NET Core Health Checks liveness probe (Railway health-gate / AC-2) ────────
// Returns HTTP 200 {"status":"Healthy"} — no auth required (liveness probe only).
app.MapHealthChecks("/health");

// ── /healthz — detailed health check: DB + Redis status (Docker Compose / internal monitoring) ─
HealthCheckEndpoint.MapHealthCheck(app);

// ── Startup: apply EF Core migrations then seed reference data (AC2) ─────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    Console.WriteLine("[Startup] Migrations applied successfully.");
    await SeedData.SeedSpecialtiesAsync(db);
}

app.Run();
