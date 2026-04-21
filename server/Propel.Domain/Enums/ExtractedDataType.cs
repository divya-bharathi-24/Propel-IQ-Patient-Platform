namespace Propel.Domain.Enums;

/// <summary>
/// Classifies the category of a single AI-extracted data field from a clinical document.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum ExtractedDataType
{
    Vital,
    History,
    Medication,
    Allergy,
    Diagnosis
}
