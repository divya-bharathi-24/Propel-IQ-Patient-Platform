# Task - TASK_002

## Requirement Reference

- User Story: [us_009] (extracted from input)
- Story Location: [.propel/context/tasks/EP-DATA/us_009/us_009.md]
- Acceptance Criteria:
  - **AC-4**: Given AES-256 at-rest encryption is configured via pgcrypto, When sensitive patient columns are stored, Then the encryption function is applied at the application layer before DB write, and decryption restores the original value correctly.
- Edge Case:
  - What happens if pgcrypto is not available at runtime? The encryption service checks `pg_extension` on startup and throws `InvalidOperationException` if `pgcrypto` is absent. This is a fail-fast guard consistent with the environment variable validation pattern established in US_005.
  - What happens if the encryption key is rotated? The encryption key is read exclusively from the `ENCRYPTION_KEY` environment variable — never from code or config files. Key rotation requires re-encrypting existing rows, which is a separate operational procedure outside the scope of this task.

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

| Layer    | Technology                    | Version |
| -------- | ----------------------------- | ------- |
| Backend  | ASP.NET Core Web API          | .net 10  |
| ORM      | Entity Framework Core         | 9.x     |
| Database | PostgreSQL                    | 16+     |
| DB Driver| Npgsql                        | 9.x     |
| Crypto   | pgcrypto (PostgreSQL built-in) | PostgreSQL 16 |
| Language | C#                            | 13      |
| AI/ML    | N/A                           | N/A     |
| Mobile   | N/A                           | N/A     |

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

Implement `IEncryptionService` and its `PgcryptoEncryptionService` implementation in `PropelIQ.Infrastructure/Security/`. The service wraps PostgreSQL's `pgcrypto` functions (`pgp_sym_encrypt` / `pgp_sym_decrypt`) invoked via raw Npgsql `NpgsqlConnection` queries — not through EF Core — to encrypt and decrypt sensitive patient string fields (e.g., `Patient.DateOfBirth`, `Patient.Phone`, `Patient.Address`) at the application layer before write and after read respectively.

The encryption key is sourced exclusively from the `ENCRYPTION_KEY` environment variable (OWASP A02 — no hardcoded secrets). The service is registered in DI as `Singleton` and injected into repository classes that handle PHI fields. The interface is defined in `PropelIQ.Application/` to maintain clean architecture layering.

**Encryption algorithm**: `pgp_sym_encrypt(plaintext, key, 'cipher-algo=aes256')` — AES-256 symmetric encryption via pgcrypto's OpenPGP-compatible symmetric cipher. Returns `bytea`. Stored in the database as `TEXT` via Base64 encoding.

**Decryption**: `pgp_sym_decrypt(decode(ciphertext, 'base64'), key)` — restores original plaintext.

This pattern is chosen over EF Core value converters because:
1. Value converters run in the EF pipeline and cannot inject the runtime encryption key from DI.
2. Raw Npgsql queries give explicit control over when and how encryption is applied.
3. The repository pattern (write-only for audit, PHI-aware for patient) keeps encryption concerns centralized.

## Dependent Tasks

- US_009 `task_001_db_extensions_seed_data_migration.md` — `pgcrypto` extension must be active in the database before the service can be tested

## Impacted Components

| Component | Action | Notes |
| --------- | ------ | ----- |
| `server/src/PropelIQ.Application/Interfaces/IEncryptionService.cs` | CREATE | Interface with `Encrypt(string plaintext)` and `Decrypt(string ciphertext)` methods |
| `server/src/PropelIQ.Infrastructure/Security/PgcryptoEncryptionService.cs` | CREATE | Implementation using raw Npgsql queries to call `pgp_sym_encrypt` / `pgp_sym_decrypt` |
| `server/src/PropelIQ.Api/Program.cs` | MODIFY | Register `IEncryptionService` → `PgcryptoEncryptionService` as Singleton; add fail-fast guard for `ENCRYPTION_KEY` env var |

## Implementation Plan

1. **Create `IEncryptionService.cs`** in `PropelIQ.Application/Interfaces/` — Declare two methods: `string Encrypt(string plaintext)` and `string Decrypt(string ciphertext)`. Both are synchronous — pgcrypto calls are fast scalar queries. Add a `bool IsAvailable { get; }` property for the startup health check.

2. **Create `PgcryptoEncryptionService.cs`** in `PropelIQ.Infrastructure/Security/` — Constructor accepts `IConfiguration config` and `NpgsqlDataSource dataSource`. Read `ENCRYPTION_KEY` from env via `config["ENCRYPTION_KEY"]`; throw `InvalidOperationException("ENCRYPTION_KEY environment variable is required")` if null or empty (fail-fast, OWASP A02). Store the key in a `private readonly string _key` field.

3. **Implement `Encrypt(string plaintext)`** — Open a `NpgsqlConnection` from `dataSource`. Execute scalar: `SELECT encode(pgp_sym_encrypt($1::text, $2::text, 'cipher-algo=aes256'), 'base64')` with parameters `[plaintext, _key]`. Return the Base64-encoded ciphertext string. Dispose connection after use.

4. **Implement `Decrypt(string ciphertext)`** — Execute scalar: `SELECT pgp_sym_decrypt(decode($1::text, 'base64'), $2::text)` with parameters `[ciphertext, _key]`. Return the plaintext string.

5. **Implement `IsAvailable` property** — Executes `SELECT COUNT(*) FROM pg_extension WHERE extname = 'pgcrypto'` and returns `true` if count > 0. Called once at startup; result cached in a `private bool _isAvailable` field set in constructor.

6. **Modify `Program.cs`** — Add fail-fast guard: `if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENCRYPTION_KEY"))) throw new InvalidOperationException("ENCRYPTION_KEY is required");`. Register: `builder.Services.AddSingleton<IEncryptionService, PgcryptoEncryptionService>();`. Add health check that verifies `IEncryptionService.IsAvailable`.

7. **Add NpgsqlDataSource to DI** — Verify `NpgsqlDataSource` is registered in DI (added when configuring the Npgsql EF Core provider via `UseNpgsql`). If using `NpgsqlDataSourceBuilder`, confirm it is registered as `Singleton` before `PgcryptoEncryptionService` is resolved.

8. **Write usage example in XML doc comment on `IEncryptionService`** — Document the expected call pattern: encrypt before `DbContext.SaveChangesAsync()`, decrypt after reading from `DbContext`. Note that the `Patient` repository is the primary consumer and PHI field names are: `DateOfBirth`, `Phone`, `Address`.

## Current Project State

```
server/src/
├── PropelIQ.Application/
│   └── Interfaces/
│       ├── IPatientRepository.cs    # Existing — primary consumer of encryption
│       └── IEncryptionService.cs    # To be created
└── PropelIQ.Infrastructure/
    └── Security/
        └── PgcryptoEncryptionService.cs  # To be created
```

_Update this tree during execution based on the completion of dependent tasks._

## Expected Changes

| Action | File Path | Description |
| ------ | --------- | ----------- |
| CREATE | `server/src/PropelIQ.Application/Interfaces/IEncryptionService.cs` | `Encrypt`, `Decrypt`, `IsAvailable` contract |
| CREATE | `server/src/PropelIQ.Infrastructure/Security/PgcryptoEncryptionService.cs` | pgcrypto AES-256 implementation via raw Npgsql scalar queries |
| MODIFY | `server/src/PropelIQ.Api/Program.cs` | Fail-fast `ENCRYPTION_KEY` guard + Singleton DI registration |

## External References

- [pgcrypto — `pgp_sym_encrypt` / `pgp_sym_decrypt` (PostgreSQL 16 docs)](https://www.postgresql.org/docs/current/pgcrypto.html#PGCRYPTO-PGP-ENC-FUNCS)
- [pgcrypto — `cipher-algo=aes256` option](https://www.postgresql.org/docs/current/pgcrypto.html#PGCRYPTO-PGP-ENC-FUNCS-OPTIONS)
- [Npgsql — `NpgsqlDataSource` and raw commands](https://www.npgsql.org/doc/basic-usage.html#parameters)
- [NFR-004: AES-256 at-rest encryption requirement (design.md)](../.propel/context/docs/design.md#nfr-004)
- [AG-2: HIPAA compliance by design (design.md)](../.propel/context/docs/design.md#ag-2)
- [OWASP A02 — Cryptographic Failures (no hardcoded keys)](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [ASP.NET Core — Environment variable configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#environment-variables)

## Build Commands

```bash
# Build Application and Infrastructure projects
cd server
dotnet build src/PropelIQ.Application/PropelIQ.Application.csproj
dotnet build src/PropelIQ.Infrastructure/PropelIQ.Infrastructure.csproj

# Full solution build
dotnet build PropelIQ.sln

# Encrypt/decrypt round-trip test (requires pgcrypto active — run after task_001 migration)
psql $DATABASE_URL -c "
  SELECT pgp_sym_decrypt(
    pgp_sym_encrypt('John Doe DOB 1985-03-14', 'test-key-32-bytes-minimum-length!', 'cipher-algo=aes256'),
    'test-key-32-bytes-minimum-length!'
  ) AS decrypted_value;"
# Expected: 'John Doe DOB 1985-03-14'
```

## Implementation Validation Strategy

- [ ] `dotnet build PropelIQ.Application` exits 0 — `IEncryptionService` interface compiles
- [ ] `dotnet build PropelIQ.Infrastructure` exits 0 — `PgcryptoEncryptionService` compiles
- [ ] `PgcryptoEncryptionService` constructor throws `InvalidOperationException` if `ENCRYPTION_KEY` is null or empty (AC-4 / OWASP A02)
- [ ] `Encrypt()` returns non-null, non-empty Base64 string for any plaintext input (AC-4)
- [ ] `Decrypt(Encrypt(plaintext))` returns original `plaintext` — round-trip test passes (AC-4)
- [ ] `IsAvailable` returns `true` when pgcrypto extension is active
- [ ] `Program.cs` fail-fast guard prevents startup if `ENCRYPTION_KEY` is absent
- [ ] `dotnet build PropelIQ.sln` exits 0 — no cross-project reference errors

## Implementation Checklist

- [ ] Create `IEncryptionService.cs` — `Encrypt`, `Decrypt`, `IsAvailable` members declared
- [ ] Create `PgcryptoEncryptionService.cs` — fail-fast on missing `ENCRYPTION_KEY`, AES-256 via `cipher-algo=aes256`
- [ ] `Encrypt()` uses Base64 encoding (`encode(..., 'base64')`) for TEXT storage compatibility
- [ ] `Decrypt()` uses `decode(..., 'base64')` before passing to `pgp_sym_decrypt`
- [ ] `IsAvailable` caches the pg_extension check result to avoid repeated DB queries
- [ ] Modify `Program.cs` — add env var guard and Singleton registration
- [ ] Run `dotnet build PropelIQ.sln` — confirm zero errors
- [ ] Run psql round-trip test against Neon staging — confirm decrypted value matches original (AC-4)
