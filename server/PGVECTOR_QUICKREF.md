# pgvector Quick Reference Card

## ?? IMMEDIATE ACTION

Your application is **ready to run** without pgvector!

```powershell
dotnet run --project Propel.Api.Gateway
```

---

## ?? What Changed

4 code sections commented out in 3 files:
- `Program.cs`: Lines ~154, ~167
- `AppDbContext.cs`: Line ~69
- `DocumentChunkEmbeddingConfiguration.cs`: Lines ~48-62

---

## ?? Install pgvector Later (5 minutes)

```powershell
# Step 1: Run setup script
.\setup-pgvector.ps1

# Step 2: Uncomment code in 3 files
# - Program.cs line ~154: dataSourceBuilder.UseVector();
# - Program.cs line ~167: o => o.UseVector()
# - AppDbContext.cs line ~69: HasPostgresExtension("vector")
# - DocumentChunkEmbeddingConfiguration.cs lines ~48-62: Vector column config

# Step 3: Restart app
dotnet run --project Propel.Api.Gateway
```

---

## ?? Full Documentation

- **Complete Guide**: `PGVECTOR_SETUP_GUIDE.md`
- **Summary**: `PGVECTOR_DISABLE_SUMMARY.md`

---

## ? One-Line Install (Docker)

```powershell
docker run -d --name propel-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=propeliq -p 5432:5432 pgvector/pgvector:pg16
```

Then uncomment the 3 lines and restart.

---

## ? Status

- **Build**: ? Successful
- **Runtime**: ? Works without pgvector
- **Features**: ?? Vector features disabled
- **Data**: ? No data loss
- **Reversible**: ? Just uncomment 3 lines

---

**You're all set! Run your app now. ??**
