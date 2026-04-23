namespace Propel.Modules.Patient.Exceptions;

/// <summary>
/// Thrown when a <c>PUT /api/intake/{appointmentId}</c> request passes optimistic concurrency
/// validation but fails FluentValidation because one or more required demographic fields are
/// missing (US_017, AC-3).
/// <para>
/// Maps to HTTP 422 Unprocessable Entity. Before throwing, the handler persists the submitted
/// form data to the <c>draftData</c> JSONB column so the patient can resume without losing input.
/// </para>
/// </summary>
public sealed class IntakeMissingFieldsException : Exception
{
    /// <summary>
    /// Field paths that failed the required-field validation rule
    /// (e.g. <c>"demographics.name"</c>, <c>"demographics.dob"</c>).
    /// </summary>
    public IReadOnlyList<string> MissingFields { get; }

    public IntakeMissingFieldsException(IReadOnlyList<string> missingFields)
        : base("One or more required intake fields are missing.")
    {
        MissingFields = missingFields;
    }
}
