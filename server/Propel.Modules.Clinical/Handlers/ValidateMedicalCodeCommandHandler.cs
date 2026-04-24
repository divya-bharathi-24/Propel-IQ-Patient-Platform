using MediatR;
using Propel.Domain.Dtos;
using Propel.Domain.Interfaces;
using Propel.Modules.Clinical.Commands;

namespace Propel.Modules.Clinical.Handlers;

/// <summary>
/// Handles <see cref="ValidateMedicalCodeCommand"/> for
/// <c>POST /api/medical-codes/validate</c> (EP-008-II/us_043, task_002, AC-4).
///
/// <list type="number">
///   <item>Delegates to <see cref="ICodeReferenceLibrary.Validate"/> — no direct DB access.</item>
///   <item>Returns <see cref="CodeValidationResult"/> with <c>Valid = true</c> and the normalized code on success, or <c>Valid = false</c> with a human-readable message on failure.</item>
/// </list>
///
/// No patient or user identity is involved — this endpoint is code-content-only (OWASP A01).
/// The singleton <see cref="ICodeReferenceLibrary"/> is thread-safe by design (immutable reference data).
/// </summary>
public sealed class ValidateMedicalCodeCommandHandler
    : IRequestHandler<ValidateMedicalCodeCommand, CodeValidationResult>
{
    private readonly ICodeReferenceLibrary _codeLibrary;

    public ValidateMedicalCodeCommandHandler(ICodeReferenceLibrary codeLibrary)
    {
        _codeLibrary = codeLibrary;
    }

    public Task<CodeValidationResult> Handle(
        ValidateMedicalCodeCommand command,
        CancellationToken cancellationToken)
    {
        var lookup = _codeLibrary.Validate(command.Code, command.CodeType);

        var result = new CodeValidationResult(
            Valid:          lookup.IsValid,
            CodeType:       command.CodeType.ToString(),
            NormalizedCode: lookup.NormalizedCode,
            Message:        lookup.Message);

        return Task.FromResult(result);
    }
}
