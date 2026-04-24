using Propel.Domain.Enums;

namespace Propel.Domain.Dtos;

/// <summary>
/// A single AI-suggested medical code returned by the ICD-10 or CPT tool-calling pipeline
/// (EP-008-II/us_042, task_001, AC-1, AC-2, AC-4, AIR-Q03).
/// <para>
/// Rules:
/// <list type="bullet">
///   <item><description><see cref="LowConfidence"/> is computed by the orchestrator: <c>true</c> when <see cref="Confidence"/> &lt; 0.80 (AC-4, AIR-003).</description></item>
///   <item><description><see cref="SourceDocumentId"/> references the clinical document that produced the evidence; <see cref="Guid.Empty"/> when no single document can be identified.</description></item>
///   <item><description>Only valid ICD-10-CM or CPT codes are included — hallucinated codes outside the reference libraries are rejected by <c>MedicalCodeSchemaValidator</c> (AC-3, AIR-Q03).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed record MedicalCodeSuggestionDto(
    /// <summary>Standard ICD-10-CM or CPT alphanumeric code (e.g. "J18.9", "99213").</summary>
    string Code,

    /// <summary>Identifies whether this suggestion is an ICD-10 diagnostic or CPT procedure code.</summary>
    MedicalCodeType CodeType,

    /// <summary>Human-readable code description from the reference library.</summary>
    string Description,

    /// <summary>
    /// AI confidence score in [0, 1].  Scores below 0.80 set <see cref="LowConfidence"/> = <c>true</c>.
    /// </summary>
    decimal Confidence,

    /// <summary>
    /// Primary key of the clinical document that provided the evidence for this suggestion.
    /// Set to <see cref="Guid.Empty"/> when the evidence spans multiple documents.
    /// </summary>
    Guid SourceDocumentId,

    /// <summary>
    /// <c>true</c> when <see cref="Confidence"/> &lt; 0.80 — highlighted for staff scrutiny (AC-4, AIR-003).
    /// </summary>
    bool LowConfidence);
