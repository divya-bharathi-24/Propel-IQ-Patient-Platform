using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Specialty"/> entity (task_002).
/// Table: <c>specialties</c>
/// Provider specialty reference / lookup table — seeded via <c>HasData</c> with two starter
/// entries for local development and smoke tests. Additional specialties are inserted at
/// runtime by <see cref="SeedData.SeedSpecialtiesAsync"/> which is idempotent (skips when table has rows).
/// </summary>
public sealed class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    // Fixed GUIDs for HasData seed rows — stable across migrations
    private static readonly Guid GeneralPracticeId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CardiologyId = new("00000000-0000-0000-0000-000000000002");

    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("specialties");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Name)
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(s => s.Description)
               .HasMaxLength(500);

        // Two starter specialties for local dev and smoke tests (task_002 step 3)
        builder.HasData(
            new Specialty { Id = GeneralPracticeId, Name = "General Practice", Description = "Primary care and general health services" },
            new Specialty { Id = CardiologyId, Name = "Cardiology", Description = "Heart and cardiovascular system care" }
        );
    }
}
