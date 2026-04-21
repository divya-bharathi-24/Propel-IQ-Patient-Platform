namespace Propel.Domain.Enums;

/// <summary>
/// Identifies the medical coding system for an AI-suggested code.
/// Stored as string in the database for human-readable audit logs.
/// </summary>
public enum MedicalCodeType
{
    ICD10,
    CPT
}
