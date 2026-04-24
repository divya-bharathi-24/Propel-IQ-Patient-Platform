using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Propel.Domain.Entities;

namespace Propel.Api.Gateway.Data.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="DataConflict"/> entity (task_004).
/// Table: <c>data_conflicts</c>
/// Key design decisions:
///   - Two distinct FK relationships to <c>clinical_documents</c> via
///     <c>source_document_id1</c> and <c>source_document_id2</c> capture the pair of
///     conflicting source documents (DR-008, FR-035).
///   - Resolution fields (<c>resolved_value</c>, <c>resolved_by</c>, <c>resolved_at</c>,
///     <c>resolution_note</c>) are nullable to represent the unresolved state (AC-3).
///   - FK <c>patient_id</c> uses <see cref="DeleteBehavior.Cascade"/> — patient deletion
///     cascades to conflict records (DR-008).
///   - FK <c>source_document_id1/2</c> use <see cref="DeleteBehavior.Restrict"/> — document
///     deletion is blocked while conflicts reference them to preserve audit trail (DR-008).
///   - FK <c>resolved_by</c> uses <see cref="DeleteBehavior.Restrict"/> — staff user deletion
///     is blocked while conflicts reference them (AC-3).
///   - Index 1: composite <c>(patient_id, resolution_status, severity)</c> for conflict-gate
///     query and filtered patient views (AC-4).
///   - Index 2: standard <c>patient_id</c> for all-conflicts retrieval.
///   - Index 3: partial unique <c>(patient_id, field_name, source_document_id1, source_document_id2)</c>
///     WHERE <c>resolution_status = 'Unresolved'</c> — enforces idempotency on re-runs (AC-1 edge case).
/// </summary>
public sealed class DataConflictConfiguration : IEntityTypeConfiguration<DataConflict>
{
    public void Configure(EntityTypeBuilder<DataConflict> builder)
    {
        builder.ToTable("data_conflicts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.FieldName)
               .HasMaxLength(256)
               .IsRequired();

        builder.Property(d => d.Value1)
               .HasMaxLength(2000)
               .IsRequired();

        builder.Property(d => d.Value2)
               .HasMaxLength(2000)
               .IsRequired();

        builder.Property(d => d.Severity)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired()
               .HasDefaultValue(Propel.Domain.Enums.DataConflictSeverity.Warning);

        builder.Property(d => d.ResolutionStatus)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired()
               .HasDefaultValueSql("'Unresolved'");

        builder.Property(d => d.ResolvedValue)
               .HasMaxLength(2000);

        builder.Property(d => d.ResolvedBy);

        builder.Property(d => d.ResolvedAt)
               .HasColumnType("timestamp with time zone");

        builder.Property(d => d.ResolutionNote)
               .HasMaxLength(1000);

        builder.Property(d => d.DetectedAt)
               .HasColumnType("timestamp with time zone")
               .HasDefaultValueSql("now()");

        // FK: data_conflicts → patients (Cascade — patient deletion removes all conflict records, DR-008)
        builder.HasOne(d => d.Patient)
               .WithMany()
               .HasForeignKey(d => d.PatientId)
               .OnDelete(DeleteBehavior.Cascade);

        // FK: data_conflicts → clinical_documents via SourceDocumentId1 (Restrict — preserve audit trail, DR-008)
        builder.HasOne(d => d.SourceDocument1)
               .WithMany()
               .HasForeignKey(d => d.SourceDocumentId1)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: data_conflicts → clinical_documents via SourceDocumentId2 (Restrict — preserve audit trail, DR-008)
        builder.HasOne(d => d.SourceDocument2)
               .WithMany()
               .HasForeignKey(d => d.SourceDocumentId2)
               .OnDelete(DeleteBehavior.Restrict);

        // FK: data_conflicts → users via ResolvedBy (Restrict — block user deletion while conflicts reference them, AC-3)
        builder.HasOne(d => d.ResolvedByUser)
               .WithMany()
               .HasForeignKey(d => d.ResolvedBy)
               .OnDelete(DeleteBehavior.Restrict);

        // Index 1 — composite query index: supports GetCriticalUnresolvedCountAsync and filtered patient views (AC-4)
        builder.HasIndex(d => new { d.PatientId, d.ResolutionStatus, d.Severity })
               .HasDatabaseName("ix_data_conflicts_patient_status_severity");

        // Index 2 — standard patient index: supports all-conflicts retrieval for a patient
        builder.HasIndex(d => d.PatientId)
               .HasDatabaseName("ix_data_conflicts_patient_id");

        // Index 3 — partial unique index: enforces idempotency on conflict detection re-runs (AC-1 edge case)
        // EF Core 9 partial index via HasFilter (PostgreSQL dialect uses double-quoted identifiers for snake_case columns)
        builder.HasIndex(d => new { d.PatientId, d.FieldName, d.SourceDocumentId1, d.SourceDocumentId2 })
               .IsUnique()
               .HasDatabaseName("ix_data_conflicts_idempotency")
               .HasFilter("\"resolution_status\" = 'Unresolved'");

        // Legacy conflict-gate partial index from PatientProfileVerification migration (us_041/task_004).
        // Retained in configuration so the snapshot remains consistent with the DB.
        builder.HasIndex(d => d.PatientId)
               .HasDatabaseName("ix_data_conflicts_patient_critical_unresolved")
               .HasFilter("severity = 'Critical' AND resolution_status = 'Unresolved'");
    }
}
