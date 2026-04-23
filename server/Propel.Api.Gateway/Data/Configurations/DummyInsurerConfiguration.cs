using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="DummyInsurer"/> seed-data entity (US_019, task_003).
/// Table: <c>dummy_insurers</c>
/// Key design decisions:
///   - Table is read-only at runtime — seeded via <see cref="ModelBuilder"/> <c>HasData()</c>
///     so data is managed through EF Core migrations, not application code.
///   - Five deterministic seed records are pre-loaded for <c>InsuranceSoftCheckService</c>
///     prefix matching (FR-038, FR-040).
///   - GUIDs are deterministic (fixed) so re-running migrations does not produce duplicates.
/// </summary>
public sealed class DummyInsurerConfiguration : IEntityTypeConfiguration<DummyInsurer>
{
    // Deterministic seed GUIDs — stable across migration runs
    private static readonly Guid BlueCrossId       = new("a1b2c3d4-0001-0000-0000-000000000000");
    private static readonly Guid AetnaId           = new("a1b2c3d4-0002-0000-0000-000000000000");
    private static readonly Guid UnitedHealthId    = new("a1b2c3d4-0003-0000-0000-000000000000");
    private static readonly Guid CignaId           = new("a1b2c3d4-0004-0000-0000-000000000000");
    private static readonly Guid HumanaId          = new("a1b2c3d4-0005-0000-0000-000000000000");

    public void Configure(EntityTypeBuilder<DummyInsurer> builder)
    {
        builder.ToTable("dummy_insurers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever(); // Seeded with explicit GUIDs

        builder.Property(d => d.InsurerName)
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(d => d.MemberIdPrefix)
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(d => d.IsActive)
               .IsRequired();

        // Seed data — 5 dummy insurers used by InsuranceSoftCheckService (FR-038, FR-040)
        builder.HasData(
            new DummyInsurer { Id = BlueCrossId,    InsurerName = "BlueCross Shield",   MemberIdPrefix = "BCS", IsActive = true },
            new DummyInsurer { Id = AetnaId,        InsurerName = "Aetna Health",        MemberIdPrefix = "AET", IsActive = true },
            new DummyInsurer { Id = UnitedHealthId, InsurerName = "United HealthGroup",  MemberIdPrefix = "UHG", IsActive = true },
            new DummyInsurer { Id = CignaId,        InsurerName = "Cigna Medical",       MemberIdPrefix = "CGN", IsActive = true },
            new DummyInsurer { Id = HumanaId,       InsurerName = "Humana Plus",         MemberIdPrefix = "HMN", IsActive = true }
        );
    }
}
