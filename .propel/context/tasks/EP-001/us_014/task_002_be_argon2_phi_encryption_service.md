# Task - task_002_be_argon2_phi_encryption_service

## Requirement Reference

- **User Story:** us_014 — Rate Limiting, Input Validation & Encryption Controls
- **Story Location:** `.propel/context/tasks/EP-001/us_014/us_014.md`
- **Acceptance Criteria:**
  - AC-3: All stored passwords are Argon2id hashes with a unique salt — never plaintext; verifiable by inspecting the `password_hash` column in the `patients` and `users` tables
- **Edge Cases:**
  - Excessively long strings: `MaxLength` validators in the registration handler prevent hash computation on unbounded input (enforced upstream by task_001 validation pipeline)
  - PHI fields (name, phone, dateOfBirth in `patients`) are encrypted at rest using AES-256 before persisting to the DB, decrypted transparently on read (NFR-004, NFR-013)

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

| Layer             | Technology                          | Version |
| ----------------- | ----------------------------------- | ------- |
| Backend           | ASP.NET Core Web API                | .NET 9  |
| Password Hashing  | Isopoh.Cryptography.Argon2          | Latest  |
| Encryption        | .NET System.Security.Cryptography   | .NET 9  |
| Key Management    | ASP.NET Core Data Protection API    | .NET 9  |
| ORM               | Entity Framework Core               | 9.x     |
| Testing — Unit    | xUnit + Moq                         | 2.x     |
| Database          | PostgreSQL                          | 16+     |
| AI/ML             | N/A                                 | N/A     |
| Mobile            | N/A                                 | N/A     |

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

Implement two security services centralising cryptographic controls for the entire platform:

1. **`IPasswordHasher` / `Argon2PasswordHasher`** — wraps `Isopoh.Cryptography.Argon2` with Argon2id variant, recommended OWASP parameters (memory=65536 KiB, iterations=3, parallelism=2), and automatic unique-salt generation. Replaces any ad-hoc Argon2 calls in `RegisterPatientCommandHandler`, `SetupCredentialsCommandHandler`, and future handlers. Single source of truth for password hashing across the codebase (NFR-008, DRY).

2. **`IPhiEncryptionService` / `AesGcmPhiEncryptionService`** — AES-256-GCM application-layer encryption for PHI string fields (`Patient.Name`, `Patient.Phone`, `Patient.DateOfBirth`) before they reach EF Core. Configured via ASP.NET Core Data Protection API for key management (key rotation, persistence to file system or Upstash Redis). EF Core value converters on `PatientConfiguration` transparently call `Encrypt`/`Decrypt` on read/write. This satisfies NFR-004 (AES-256 at rest) and NFR-013 (HIPAA PHI handling) without requiring a pgcrypto DB-side function.

---

## Dependent Tasks

- **US_010 task_001_fe_registration_ui** / **task_002_be_registration_api** (EP-001) — `RegisterPatientCommandHandler` currently calls Argon2 directly; this task centralises that call into `IPasswordHasher`
- **US_012 task_002_be_account_management_api** (EP-001) — `SetupCredentialsCommandHandler` similarly must use `IPasswordHasher` after this task
- **US_010 task_003_db_patient_schema** (EP-001) — `patients` table and `PatientConfiguration` must exist before EF Core value converters can be applied

---

## Impacted Components

| Status | Component / Module | Project |
| ------ | ------------------- | ------- |
| CREATE | `IPasswordHasher` interface | `Server/Infrastructure/Security/` |
| CREATE | `Argon2PasswordHasher` implementation | `Server/Infrastructure/Security/` |
| CREATE | `IPhiEncryptionService` interface | `Server/Infrastructure/Security/` |
| CREATE | `AesGcmPhiEncryptionService` implementation | `Server/Infrastructure/Security/` |
| MODIFY | `PatientConfiguration.cs` (EF Core fluent config) | Apply `HasConversion` value converters for PHI columns |
| MODIFY | `RegisterPatientCommandHandler.cs` | Replace direct Argon2 call with `IPasswordHasher.Hash(password)` |
| MODIFY | `SetupCredentialsCommandHandler.cs` | Replace direct Argon2 call with `IPasswordHasher.Hash(password)` |
| MODIFY | `Program.cs` | Register `IPasswordHasher`, `IPhiEncryptionService`, Data Protection key ring |

---

## Implementation Plan

1. **`IPasswordHasher` interface**:

   ```csharp
   public interface IPasswordHasher
   {
       string Hash(string plaintext);
       bool Verify(string plaintext, string hash);
   }
   ```

2. **`Argon2PasswordHasher`** (Argon2id variant — OWASP minimum recommended for interactive logins):

   ```csharp
   // Argon2id parameters (OWASP Password Storage Cheat Sheet 2024)
   MemoryCost = 19456,       // 19 MiB (interactive baseline)
   TimeCost = 2,             // iterations
   Parallelism = 1,          // single-threaded for free-tier CPU constraints
   ```

   - `Hash()`: generates a cryptographically random 16-byte salt (`RandomNumberGenerator.GetBytes(16)`), passes salt + plaintext to `Argon2.Hash()`, returns the encoded string (includes algorithm ID, params, salt, hash — safe to store directly)
   - `Verify()`: calls `Argon2.Verify(hash, plaintext)` — constant-time comparison (timing-attack safe)
   - Throws `ArgumentException` if plaintext is null or empty (defence-in-depth; validation layer catches it first)

3. **`IPhiEncryptionService` interface**:

   ```csharp
   public interface IPhiEncryptionService
   {
       string Encrypt(string plaintext);
       string Decrypt(string ciphertext);
   }
   ```

4. **`AesGcmPhiEncryptionService`** (AES-256-GCM):
   - Uses `AesGcm` from `System.Security.Cryptography` (.NET 9 built-in)
   - 256-bit key sourced from ASP.NET Core Data Protection API (`IDataProtectionProvider.CreateProtector("phi-fields")`) — key managed, rotated, and persisted by the Data Protection stack
   - `Encrypt()`: generates a 12-byte random nonce, encrypts plaintext to ciphertext + 16-byte auth tag, returns Base64(nonce + ciphertext + tag) as a single storable string
   - `Decrypt()`: splits stored string back into nonce, ciphertext, tag; decrypts and returns plaintext
   - Returns `null` if input is null (nullable PHI fields like `Phone` must round-trip correctly)

5. **EF Core value converters** in `PatientConfiguration.cs`:

   ```csharp
   // PatientConfiguration.Configure(EntityTypeBuilder<Patient> builder)
   builder.Property(p => p.Name)
       .HasConversion(
           v => _phiEncryptionService.Encrypt(v),
           v => _phiEncryptionService.Decrypt(v));

   builder.Property(p => p.Phone)
       .HasConversion(
           v => v == null ? null : _phiEncryptionService.Encrypt(v),
           v => v == null ? null : _phiEncryptionService.Decrypt(v));

   builder.Property(p => p.DateOfBirth)
       .HasConversion(
           v => _phiEncryptionService.Encrypt(v.ToString("yyyy-MM-dd")),
           v => DateOnly.Parse(_phiEncryptionService.Decrypt(v)));
   ```

   - Inject `IPhiEncryptionService` into `PatientConfiguration` via constructor DI (EF Core supports this via `DbContext.OnModelCreating` receiving configured services through `ModelBuilder`)

6. **Data Protection key ring configuration** in `Program.cs`:
   - `builder.Services.AddDataProtection().SetApplicationName("propeliq-platform")`
   - Key persistence: file system path for local dev; Upstash Redis for production (`.PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys")`)
   - Key lifetime: default 90-day rotation (`SetDefaultKeyLifetime(TimeSpan.FromDays(90))`)

7. **Handler refactoring**: update `RegisterPatientCommandHandler` and `SetupCredentialsCommandHandler` to inject and call `IPasswordHasher.Hash()` instead of calling `Argon2` directly. Remove any direct `Isopoh.Cryptography.Argon2` import from those handlers.

---

## Current Project State

```
Propel-IQ-Patient-Platform/
├── .propel/
├── .github/
└── (no Server/ scaffold yet — greenfield ASP.NET Core project)
```

> Update this section with actual `Server/Infrastructure/Security/` tree after project scaffold.

---

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `Server/Infrastructure/Security/IPasswordHasher.cs` | Interface: `Hash(plaintext)`, `Verify(plaintext, hash)` |
| CREATE | `Server/Infrastructure/Security/Argon2PasswordHasher.cs` | Argon2id implementation with OWASP-recommended parameters |
| CREATE | `Server/Infrastructure/Security/IPhiEncryptionService.cs` | Interface: `Encrypt(plaintext)`, `Decrypt(ciphertext)` |
| CREATE | `Server/Infrastructure/Security/AesGcmPhiEncryptionService.cs` | AES-256-GCM implementation using ASP.NET Core Data Protection |
| MODIFY | `Server/Infrastructure/Persistence/Configurations/PatientConfiguration.cs` | Add `HasConversion` EF Core value converters for Name, Phone, DateOfBirth |
| MODIFY | `Server/Modules/Auth/Commands/RegisterPatientCommandHandler.cs` | Inject `IPasswordHasher`; replace direct Argon2 call |
| MODIFY | `Server/Modules/Auth/Commands/SetupCredentialsCommandHandler.cs` | Inject `IPasswordHasher`; replace direct Argon2 call |
| MODIFY | `Server/Program.cs` | Register `IPasswordHasher`, `IPhiEncryptionService`, Data Protection key ring |

---

## External References

- [Isopoh.Cryptography.Argon2 — Argon2id](https://github.com/mheyman/Isopoh.Cryptography.Argon2)
- [OWASP Password Storage Cheat Sheet — Argon2id Parameters](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#argon2id)
- [.NET AesGcm — System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm)
- [ASP.NET Core Data Protection — Key Management](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-9.0)
- [ASP.NET Core Data Protection — Redis Key Storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-9.0#redis)
- [EF Core — Value Converters](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [HIPAA Security Rule — PHI Encryption at Rest (45 CFR §164.312(a)(2)(iv))](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

---

## Build Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run unit tests
dotnet test

# Apply EF Core migrations (value converters change column storage format)
dotnet ef database update --project Server/Server.csproj
```

---

## Implementation Validation Strategy

- [ ] Unit tests pass for `Argon2PasswordHasher.Hash()`: output is never plaintext input; two hashes of the same plaintext differ (unique salts)
- [ ] Unit tests pass for `Argon2PasswordHasher.Verify()`: correct plaintext returns `true`; incorrect returns `false` in constant time
- [ ] Unit tests pass for `AesGcmPhiEncryptionService.Encrypt()` then `.Decrypt()`: round-trip returns original plaintext
- [ ] Unit tests pass: `Encrypt(null)` returns null; `Decrypt(null)` returns null (nullable phone round-trip)
- [ ] `RegisterPatientCommandHandler` integration test: `patients.password_hash` column value in DB is not equal to the submitted plaintext password
- [ ] `patients.name` column in DB does not contain plaintext name after insert (verifiable by direct DB query)
- [ ] Data Protection key ring initialises without error at application startup
- [ ] Key persistence path is configurable via environment variable (not hardcoded)
- [ ] Integration tests pass (if applicable)

---

## Implementation Checklist

- [ ] Create `IPasswordHasher` interface with `Hash()` and `Verify()` methods
- [ ] Implement `Argon2PasswordHasher` (Argon2id, memory=19456, time=2, parallelism=1, random 16-byte salt)
- [ ] Create `IPhiEncryptionService` interface with `Encrypt()` and `Decrypt()` methods
- [ ] Implement `AesGcmPhiEncryptionService` using `System.Security.Cryptography.AesGcm` (12-byte nonce, 16-byte auth tag, Base64 storage)
- [ ] Configure Data Protection key ring in `Program.cs`: set application name, Redis key persistence for prod, 90-day key lifetime
- [ ] Add `HasConversion` EF Core value converters to `PatientConfiguration` for `Name`, `Phone`, `DateOfBirth`
- [ ] Refactor `RegisterPatientCommandHandler`: inject `IPasswordHasher`, remove direct `Argon2` import
- [ ] Refactor `SetupCredentialsCommandHandler`: inject `IPasswordHasher`, remove direct `Argon2` import
- [ ] Register `IPasswordHasher → Argon2PasswordHasher` and `IPhiEncryptionService → AesGcmPhiEncryptionService` as singletons in `Program.cs`
