namespace Propel.Modules.Patient.Audit;

/// <summary>
/// Compile-time string constants for all patient intake audit event action names written to
/// <c>audit_logs.action</c> (US_017, FR-006, NFR-013).
/// Using constants eliminates magic strings across all intake event handlers (anti-patterns rule).
/// </summary>
public static class IntakeAuditActions
{
    /// <summary>Patient or staff successfully read an existing intake record (AC-1).</summary>
    public const string IntakeRead = "IntakeRead";

    /// <summary>Patient successfully updated (UPSERTed) an intake record (AC-2).</summary>
    public const string IntakeUpdate = "IntakeUpdate";

    /// <summary>Patient successfully submitted a completed manual intake record (US_029, FR-057).</summary>
    public const string IntakeCompleted = "IntakeCompleted";

    /// <summary>AI resume session invoked — Semantic Kernel called to generate next question (US_030, AIR-S03).</summary>
    public const string IntakeAiResume = "IntakeAiResume";

    /// <summary>Patient localStorage draft synced to server (US_030, AC-3 — both applied and conflict paths).</summary>
    public const string LocalDraftSync = "LocalDraftSync";
}
