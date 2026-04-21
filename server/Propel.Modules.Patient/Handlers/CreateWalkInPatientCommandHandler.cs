using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Exceptions;
using PatientEntity = Propel.Domain.Entities.Patient;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles walk-in patient creation for the booking flow (US_012, AC-3):
/// 1. Email uniqueness check — returns 409 with <c>existingPatientId</c> if duplicate found.
/// 2. Create Patient entity: <c>EmailVerified = false</c>, <c>status = Active</c>,
///    <c>PasswordHash = null</c> (walk-in patient — no self-registration password at this step).
/// 3. Persist Patient and write immutable AuditLog entry (NFR-009, AD-7).
/// </summary>
public sealed class CreateWalkInPatientCommandHandler
    : IRequestHandler<CreateWalkInPatientCommand, CreateWalkInPatientResult>
{
    private readonly IPatientRepository _patientRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<CreateWalkInPatientCommandHandler> _logger;

    public CreateWalkInPatientCommandHandler(
        IPatientRepository patientRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<CreateWalkInPatientCommandHandler> logger)
    {
        _patientRepo = patientRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<CreateWalkInPatientResult> Handle(
        CreateWalkInPatientCommand request,
        CancellationToken cancellationToken)
    {
        // AC-3: duplicate email check — return 409 with existingPatientId for link-to-existing flow
        var existing = await _patientRepo.GetByEmailAsync(
            request.Email.ToLowerInvariant(),
            cancellationToken);

        if (existing is not null)
            throw new WalkInPatientDuplicateEmailException(existing.Id);

        var patient = new PatientEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email.ToLowerInvariant(),
            Phone = request.Phone ?? string.Empty,
            DateOfBirth = DateOnly.MinValue, // walk-in — not collected at this step
            PasswordHash = string.Empty,     // walk-in patient — no password at creation
            EmailVerified = false,
            Status = PatientStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        await _patientRepo.CreateAsync(patient, cancellationToken);

        // AuditLog INSERT (NFR-009, AC-3)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.StaffId,
            PatientId = patient.Id,
            Action = "StaffCreatedWalkInPatient",
            EntityType = nameof(PatientEntity),
            EntityId = patient.Id,
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Staff {StaffId} created walk-in Patient {PatientId}",
            request.StaffId, patient.Id);

        return new CreateWalkInPatientResult(patient.Id);
    }
}
