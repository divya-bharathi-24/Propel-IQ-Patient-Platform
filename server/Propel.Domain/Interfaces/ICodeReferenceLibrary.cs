using Propel.Domain.Enums;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Singleton service that validates a raw medical code string against the in-memory ICD-10-CM
/// and CPT standard reference libraries (EP-008-II/us_043, task_002, AC-4).
/// <para>
/// Shared between the AI pipeline (<c>MedicalCodeSchemaValidator</c>) and the Staff confirmation
/// API (<c>ValidateMedicalCodeCommandHandler</c>) so the same validation logic governs both
/// AI-generated suggestions and manually entered codes (DRY principle).
/// </para>
/// </summary>
public interface ICodeReferenceLibrary
{
    /// <summary>
    /// Validates <paramref name="code"/> against the reference library for the given
    /// <paramref name="codeType"/> and returns a structured result.
    /// </summary>
    /// <param name="code">Raw code string supplied by the caller (e.g. "J18.9", "99213").</param>
    /// <param name="codeType">Identifies which code system to validate against.</param>
    /// <returns>
    /// <see cref="CodeLookupResult"/> with <c>IsValid = true</c> and a <c>NormalizedCode</c>
    /// on success, or <c>IsValid = false</c> with an explanatory <c>Message</c> on failure.
    /// </returns>
    CodeLookupResult Validate(string code, MedicalCodeType codeType);
}

/// <summary>
/// Result of a single code lookup against the in-memory reference library.
/// </summary>
/// <param name="IsValid"><c>true</c> when the code matches the reference library.</param>
/// <param name="NormalizedCode">
/// Canonical form of the code (uppercase, no extra whitespace). <c>null</c> when <see cref="IsValid"/> is <c>false</c>.
/// </param>
/// <param name="Message">
/// Explanatory message. <c>null</c> when valid; a human-readable rejection reason when invalid.
/// </param>
public sealed record CodeLookupResult(bool IsValid, string? NormalizedCode, string? Message);
