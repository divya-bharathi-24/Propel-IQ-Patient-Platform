# server/Dockerfile — Multi-stage build for ASP.NET Core 10 API Gateway
#
# Stage 1 (build):  .NET 10 SDK — restore, publish release build.
# Stage 2 (runtime): ASP.NET 10 runtime-only image — minimal attack surface (OWASP A05).
#
# dotnet-ef global tool is installed in the build stage ONLY and is NOT copied to the
# runtime image. Migrations are applied programmatically via db.Database.MigrateAsync()
# inside Program.cs on startup, which achieves the same result as "dotnet ef database update"
# without requiring the SDK in production.

# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Install dotnet-ef for migration bundle creation (build stage only)
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"

# Copy solution manifest and all project files first to maximise layer cache hit rate.
# dotnet restore is re-run only when a .csproj or .sln changes.
COPY Propel.sln ./
COPY Propel.Api.Gateway/Propel.Api.Gateway.csproj             Propel.Api.Gateway/
COPY Propel.Modules.Admin/Propel.Modules.Admin.csproj         Propel.Modules.Admin/
COPY Propel.Modules.AI/Propel.Modules.AI.csproj               Propel.Modules.AI/
COPY Propel.Modules.Appointment/Propel.Modules.Appointment.csproj Propel.Modules.Appointment/
COPY Propel.Modules.Auth/Propel.Modules.Auth.csproj           Propel.Modules.Auth/
COPY Propel.Modules.Clinical/Propel.Modules.Clinical.csproj   Propel.Modules.Clinical/
COPY Propel.Modules.Notification/Propel.Modules.Notification.csproj Propel.Modules.Notification/
COPY Propel.Modules.Patient/Propel.Modules.Patient.csproj     Propel.Modules.Patient/

RUN dotnet restore

# Copy full source after restore to keep the restore layer cached
COPY . .

# Publish self-contained release build of the API Gateway
RUN dotnet publish Propel.Api.Gateway/Propel.Api.Gateway.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# Non-root user — OWASP A05 (Security Misconfiguration: avoid running containers as root)
RUN adduser --disabled-password --gecos "" --uid 1001 appuser

# Copy published output from build stage
COPY --from=build /app/publish .

# Copy entrypoint script, mark executable, and transfer app directory ownership to appuser
COPY docker-entrypoint.sh ./docker-entrypoint.sh
RUN chmod +x ./docker-entrypoint.sh && chown -R appuser:appuser /app

USER appuser

# Railway: Container binds plain HTTP on port 8080.
# TLS 1.2+ is terminated at Railway's edge proxy (NFR-005 satisfied externally).
# Override this with ASPNETCORE_URLS env var for local Docker Compose (see docker-compose.override.yml).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["./docker-entrypoint.sh"]
