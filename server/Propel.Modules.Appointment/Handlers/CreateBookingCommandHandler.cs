using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Enums;
using Propel.Domain.Interfaces;
using Propel.Modules.Appointment.Commands;
using Propel.Modules.Appointment.Dtos;
using Propel.Modules.Appointment.Events;
using Propel.Modules.Appointment.Exceptions;
using Propel.Modules.Appointment.Infrastructure;
using StackExchange.Redis;

namespace Propel.Modules.Appointment.Handlers;

/// <summary>
/// Handles <see cref="CreateBookingCommand"/> for <c>POST /api/appointments/book</c>
/// (US_019, AC-2, AC-3).
/// <list type="number">
///   <item><b>Step 1 — Resolve patientId from JWT claims</b> (OWASP A01 — never from request body).</item>
///   <item><b>Step 2 — Clear Redis slot-hold key</b> to release the 5-minute reservation.</item>
///   <item><b>Step 3 — INSERT Appointment</b> (status = Booked); catches <see cref="DbUpdateException"/>
///         on unique partial index violation and throws <see cref="SlotConflictException"/> → HTTP 409.</item>
///   <item><b>Step 4 — Insurance soft-check</b> via <see cref="IInsuranceSoftCheckService"/>;
///         any exception → <c>CheckPending</c> (FR-040, NFR-018). INSERT InsuranceValidation record.</item>
///   <item><b>Step 5 — Conditional WaitlistEntry INSERT</b> when <c>PreferredSlotId</c> is non-null (DR-003).</item>
///   <item><b>Step 6 — Cache invalidation</b>: deletes <c>slots:{specialtyId}:{date}</c> via
///         <see cref="ISlotCacheService.InvalidateAsync"/> (AD-8, NFR-020 staleness ≤ 5 s).</item>
///   <item><b>Step 7 — Audit log INSERT</b> via <see cref="IAuditLogRepository"/> (AD-7).</item>
/// </list>
/// </summary>
public sealed class CreateBookingCommandHandler
    : IRequestHandler<CreateBookingCommand, BookingResponseDto>
{
    private readonly IAppointmentBookingRepository _bookingRepo;
    private readonly IInsuranceSoftCheckService _insuranceCheck;
    private readonly ISlotCacheService _slotCache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPatientRepository _patientRepo;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CreateBookingCommandHandler> _logger;

    public CreateBookingCommandHandler(
        IAppointmentBookingRepository bookingRepo,
        IInsuranceSoftCheckService insuranceCheck,
        ISlotCacheService slotCache,
        IAuditLogRepository auditLogRepo,
        IPatientRepository patientRepo,
        IPublisher publisher,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CreateBookingCommandHandler> logger,
        IConnectionMultiplexer? redis = null)
    {
        _bookingRepo = bookingRepo;
        _insuranceCheck = insuranceCheck;
        _slotCache = slotCache;
        _redis = redis;
        _auditLogRepo = auditLogRepo;
        _patientRepo = patientRepo;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<BookingResponseDto> Handle(
        CreateBookingCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1 — Resolve patientId from JWT claims (OWASP A01: never from request body).
        var patientIdStr = _httpContextAccessor.HttpContext!.User
            .FindFirstValue(ClaimTypes.NameIdentifier)!;
        var patientId = Guid.Parse(patientIdStr);

        var ipAddress = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();
        var correlationId = _httpContextAccessor.HttpContext.Items["CorrelationId"]?.ToString();

        // Step 2 — Clear the Redis slot-hold key (best-effort; failure must not block booking).
        var holdKey =
            $"slot_hold:{request.SlotSpecialtyId}:{request.SlotDate:yyyy-MM-dd}:{request.SlotTimeStart:HH\\:mm}:{patientId}";
        
        if (_redis is not null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(holdKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SlotHold_ClearFailed: Could not delete hold key {HoldKey}",
                    holdKey);
            }
        }
        else
        {
            _logger.LogDebug("SlotHold_ClearSkipped: Redis unavailable (development mode)");
        }

        // Step 3 — INSERT Appointment; catch unique constraint violation → SlotConflictException (AC-3).
        var appointment = new Domain.Entities.Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            SpecialtyId = request.SlotSpecialtyId,
            Date = request.SlotDate,
            TimeSlotStart = request.SlotTimeStart,
            TimeSlotEnd = request.SlotTimeEnd,
            Status = AppointmentStatus.Booked,
            CreatedBy = patientId,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _bookingRepo.CreateAppointmentAsync(appointment, cancellationToken);
        }
        catch (SlotConflictException)
        {
            _logger.LogWarning(
                "SlotConflict: SpecialtyId={SpecialtyId} Date={Date} TimeSlot={TimeSlot} PatientId={PatientId}",
                request.SlotSpecialtyId, request.SlotDate, request.SlotTimeStart, patientId);
            throw;
        }

        // Step 4 — Insurance soft-check and InsuranceValidation INSERT (FR-040, NFR-018).
        var insuranceResult = await _insuranceCheck.CheckAsync(
            request.InsuranceName,
            request.InsuranceId,
            cancellationToken);

        var insuranceValidation = new InsuranceValidation
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            AppointmentId = appointment.Id,
            ProviderName = request.InsuranceName ?? string.Empty,
            InsuranceId = request.InsuranceId ?? string.Empty,
            ValidationResult = insuranceResult,
            ValidatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await _bookingRepo.CreateInsuranceValidationAsync(insuranceValidation, cancellationToken);

        // Step 5 — Conditional WaitlistEntry INSERT (US_023, AC-1, DR-003 FIFO ordering).
        if (request.PreferredDate.HasValue && request.PreferredTimeSlot.HasValue)
        {
            // US_023 edge case: if the preferred slot has no conflicting Booked/Arrived record,
            // it is actually available — reject with HTTP 400 so the patient books it directly.
            var preferredSlotTaken = await _bookingRepo.IsSlotTakenAsync(
                request.SlotSpecialtyId,
                request.PreferredDate.Value,
                request.PreferredTimeSlot.Value,
                cancellationToken);

            if (!preferredSlotTaken)
            {
                _logger.LogWarning(
                    "WaitlistEnrollment_SlotAvailable: PatientId={PatientId} PreferredDate={PreferredDate} PreferredTimeSlot={PreferredTimeSlot}",
                    patientId, request.PreferredDate.Value, request.PreferredTimeSlot.Value);

                throw new BusinessRuleViolationException(
                    "This slot is available — please book it directly");
            }

            var waitlistEntry = new WaitlistEntry
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                CurrentAppointmentId = appointment.Id,
                PreferredDate = request.PreferredDate.Value,
                PreferredTimeSlot = request.PreferredTimeSlot.Value,
                EnrolledAt = DateTime.UtcNow,
                Status = WaitlistStatus.Active
            };

            await _bookingRepo.CreateWaitlistEntryAsync(waitlistEntry, cancellationToken);

            _logger.LogInformation(
                "WaitlistEntry created: PatientId={PatientId} AppointmentId={AppointmentId} PreferredDate={PreferredDate} PreferredTimeSlot={PreferredTimeSlot}",
                patientId, appointment.Id, request.PreferredDate.Value, request.PreferredTimeSlot.Value);
        }

        // Step 6 — Invalidate slot availability cache (AD-8, NFR-020 staleness ≤ 5 s).
        var specialtyIdStr = request.SlotSpecialtyId.ToString();
        await _slotCache.InvalidateAsync(specialtyIdStr, request.SlotDate, cancellationToken);

        // Step 7 — Audit log INSERT via write-only repository (AD-7).
        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = patientId,
            PatientId = patientId,
            Role = "Patient",
            Action = "AppointmentBooked",
            EntityType = nameof(Domain.Entities.Appointment),
            EntityId = appointment.Id,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Appointment booked: AppointmentId={AppointmentId} PatientId={PatientId} InsuranceStatus={InsuranceStatus}",
            appointment.Id, patientId, insuranceResult);

        // Fetch specialty name for the response DTO.
        var specialtyName = await _bookingRepo.GetSpecialtyNameAsync(
            request.SlotSpecialtyId, cancellationToken) ?? string.Empty;

        var referenceNumber = $"APT-{appointment.Id.ToString("N")[..8].ToUpperInvariant()}";

        // Step 8 — Publish BookingConfirmedEvent to trigger async PDF + email delivery (US_021, AC-2).
        // Patient is fetched here for email and name — not available from JWT claims.
        // Publish is fire-and-forget from the HTTP response perspective (AG-6, NFR-018):
        // BookingConfirmedEventHandler degrades gracefully on delivery failure.
        var patient = await _patientRepo.GetByIdAsync(patientId, cancellationToken);
        if (patient is not null)
        {
            await _publisher.Publish(
                new BookingConfirmedEvent(
                    AppointmentId: appointment.Id,
                    PatientId: patientId,
                    PatientEmail: patient.Email,
                    PatientName: patient.Name,
                    SpecialtyName: specialtyName,
                    ClinicName: "Propel IQ Clinic",
                    AppointmentDate: request.SlotDate,
                    TimeSlotStart: request.SlotTimeStart,
                    TimeSlotEnd: request.SlotTimeEnd,
                    ReferenceNumber: referenceNumber),
                cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "BookingConfirmedEvent not published: patient not found. " +
                "AppointmentId={AppointmentId} PatientId={PatientId}",
                appointment.Id, patientId);
        }

        return new BookingResponseDto(
            AppointmentId: appointment.Id,
            ReferenceNumber: referenceNumber,
            Date: request.SlotDate,
            TimeSlotStart: request.SlotTimeStart,
            SpecialtyName: specialtyName,
            InsuranceStatus: insuranceResult);
    }
}
