using MediatR;
using Propel.Modules.Patient.Dtos;

namespace Propel.Modules.Patient.Commands;

/// <summary>
/// MediatR command to resume an AI intake session from a partially filled manual form (US_030, AC-2).
/// <para>
/// Receives the currently filled <see cref="IntakeFieldMap"/> from a patient switching from
/// Manual → AI mode. The handler builds a condensed context summary from non-null fields,
/// calls Semantic Kernel to determine the next unanswered intake question, and returns it so
/// the frontend can initialise the AI chat mid-conversation.
/// </para>
/// <para>
/// <c>PatientId</c> is extracted from the JWT <c>sub</c> claim in the controller — never from
/// the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed record IntakeSessionResumeCommand(
    Guid AppointmentId,
    IntakeFieldMap ExistingFields,
    Guid PatientId) : IRequest<IntakeSessionResumeResult>;

/// <summary>
/// Result returned by <see cref="IntakeSessionResumeCommand"/>.
/// <para>
/// Response shape matches what <c>AiIntakeChatComponent.initWithContext()</c> expects:
/// <c>{ nextQuestion, contextSummary }</c>.
/// </para>
/// </summary>
public sealed record IntakeSessionResumeResult(
    string NextQuestion,
    string ContextSummary);
