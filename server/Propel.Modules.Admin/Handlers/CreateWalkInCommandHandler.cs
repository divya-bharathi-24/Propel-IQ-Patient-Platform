using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Dtos;
using Propel.Modules.Admin.Enums;
using Propel.Modules.Patient.Exceptions;
using PatientEntity = Propel.Domain.Entities.Patient;

namespace Propel.Modules.Admin.Handlers;

/// <summary>
/// Handles <see cref="CreateWalkInCommand"/> for <c>POST /api/staff/walkin</c> (US_026, AC-2, AC-3).
/// <list type="number">
///   <item><b>Step 1 — Patient resolution by mode</b>:
///     <list type="bullet">
///       <item><c>Create</c>: duplicate-email check → throw <see cref="WalkInPatientDuplicateEmailException"/> (409 with existingPatientId) on match;
///             build new <c>Patient</c> entity for atomic insert.</item>
///       <item><c>Link</c>: load patient by <c>PatientId</c> → throw <see cref="KeyNotFoundException"/> (404) if absent.</item>
///       <item><c>Anonymous</c>: <c>patientId = null</c>, <c>anonymousVisitId = Guid.NewGuid()</c>.</item>
///     </list>
///   </item>
///   <item><b>Step 2 — Slot availability check</b>: if a time slot is requested and already has a
///         <c>Booked</c>/<c>Arrived</c> appointment, set <c>queuedOnly = true</c> and clear slot fields.</item>
///   <item><b>Step 3 — Build Appointment entity</b> with resolved patient and slot data.</item>
///   <item><b>Step 4 — Compute next queue position</b> for the requested date.</item>
///   <item><b>Step 5 — Build QueueEntry entity</b> (status = Waiting).</item>
///   <item><b>Step 6 — Atomic commit</b>: single <c>SaveChangesAsync</c> via <see cref="IStaffWalkInRepository.CreateWalkInAsync"/>.</item>
///   <item><b>Step 7 — Audit log INSERT</b> (action = "WalkInBooked", AD-7).</item>
/// </list>
/// <para>
/// <c>StaffId</c> is resolved from JWT claims in the controller and passed via <see cref="CreateWalkInCommand.StaffId"/>
/// — never accepted from the request body (OWASP A01 — Broken Access Control).
/// </para>
/// </summary>
public sealed class CreateWalkInCommandHandler
    : IRequestHandler<CreateWalkInCommand, WalkInResponseDto>
{
    private readonly IStaffWalkInRepository _walkInRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<CreateWalkInCommandHandler> _logger;

    public CreateWalkInCommandHandler(
        IStaffWalkInRepository walkInRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<CreateWalkInCommandHandler> logger)
    {
        _walkInRepo = walkInRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    public async Task<WalkInResponseDto> Handle(
        CreateWalkInCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Patient resolution by mode
        PatientEntity? newPatient = null;
        Guid? resolvedPatientId = null;
        Guid? anonymousVisitId = null;

        switch (request.Mode)
        {
            case WalkInMode.Create:
                // Duplicate-email check before INSERT (AC-2 edge case — detect before unique constraint fires)
                var normalizedEmail = request.Email!.ToLowerInvariant();
                var existing = await _walkInRepo.GetPatientByEmailAsync(
                    normalizedEmail, cancellationToken);

                if (existing is not null)
                {
                    _logger.LogWarning(
                        "WalkIn_DuplicateEmail: email={Email} existingPatientId={ExistingId} staffId={StaffId}",
                        normalizedEmail, existing.Id, request.StaffId);
                    throw new WalkInPatientDuplicateEmailException(existing.Id);
                }

                newPatient = new PatientEntity
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name!,
                    Email = normalizedEmail,
                    Phone = request.ContactNumber ?? string.Empty,
                    DateOfBirth = DateOnly.MinValue, // not collected during walk-in creation
                    PasswordHash = string.Empty,     // no password at walk-in creation
                    EmailVerified = false,
                    Status = PatientStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                resolvedPatientId = newPatient.Id;
                break;

            case WalkInMode.Link:
                var linkedPatient = await _walkInRepo.GetPatientByIdAsync(
                    request.PatientId!.Value, cancellationToken);

                if (linkedPatient is null)
                {
                    _logger.LogWarning(
                        "WalkIn_PatientNotFound: patientId={PatientId} staffId={StaffId}",
                        request.PatientId, request.StaffId);
                    throw new KeyNotFoundException(
                        $"Patient '{request.PatientId}' was not found.");
                }

                resolvedPatientId = linkedPatient.Id;
                break;

            case WalkInMode.Anonymous:
                resolvedPatientId = null;
                anonymousVisitId = Guid.NewGuid();
                break;
        }

        // Step 3 — Slot availability check
        TimeOnly? slotStart = request.TimeSlotStart;
        TimeOnly? slotEnd = request.TimeSlotEnd;
        bool queuedOnly = false;

        if (slotStart.HasValue)
        {
            bool slotBooked = await _walkInRepo.IsSlotBookedAsync(
                request.SpecialtyId, request.Date, slotStart, cancellationToken);

            if (slotBooked)
            {
                _logger.LogInformation(
                    "WalkIn_SlotFull: specialtyId={SpecialtyId} date={Date} slotStart={SlotStart} — queued-only",
                    request.SpecialtyId, request.Date, slotStart);
                queuedOnly = true;
                slotStart = null;
                slotEnd = null;
            }
        }

        // Step 4 — Build Appointment entity
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = resolvedPatientId,
            AnonymousVisitId = anonymousVisitId,
            SpecialtyId = request.SpecialtyId,
            Date = request.Date,
            TimeSlotStart = slotStart,
            TimeSlotEnd = slotEnd,
            Status = AppointmentStatus.Booked,
            CreatedBy = request.StaffId,
            CreatedAt = DateTime.UtcNow
        };

        // Step 5 — Compute next queue position for the date
        int nextPosition = await _walkInRepo.GetNextQueuePositionAsync(
            request.Date, cancellationToken);

        // Step 6 — Build QueueEntry entity (status = Waiting)
        var queueEntry = new QueueEntry
        {
            Id = Guid.NewGuid(),
            PatientId = resolvedPatientId,
            AppointmentId = appointment.Id,
            Position = nextPosition,
            ArrivalTime = DateTime.UtcNow,
            Status = QueueEntryStatus.Waiting
        };

        // Step 7 — Atomic commit (Patient? + Appointment + QueueEntry in one SaveChangesAsync)
        await _walkInRepo.CreateWalkInAsync(newPatient, appointment, queueEntry, cancellationToken);

        _logger.LogInformation(
            "WalkInBooked: appointmentId={AppointmentId} mode={Mode} queuedOnly={QueuedOnly} " +
            "position={Position} staffId={StaffId}",
            appointment.Id, request.Mode, queuedOnly, nextPosition, request.StaffId);

        // Step 8 — Audit log INSERT (AD-7, immutable record)
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.StaffId,
            PatientId = resolvedPatientId,
            Action = "WalkInBooked",
            EntityType = nameof(Appointment),
            EntityId = appointment.Id,
            Details = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new { mode = request.Mode.ToString(), queuedOnly })),
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        return new WalkInResponseDto(
            appointment.Id,
            anonymousVisitId,
            queuedOnly,
            nextPosition);
    }
}
