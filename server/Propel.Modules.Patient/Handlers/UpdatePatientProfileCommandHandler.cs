using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Entities;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Commands;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Exceptions;
using PatientEntity = Propel.Domain.Entities.Patient;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="UpdatePatientProfileCommand"/> for <c>PATCH /api/patients/me</c>
/// (US_015, AC-2, AC-3, AC-4).
/// <list type="number">
///   <item>Loads the patient; throws <see cref="KeyNotFoundException"/> (→ HTTP 404) if absent.</item>
///   <item>Compares the current <c>xmin</c>-derived ETag with <c>If-Match</c> header;
///         throws <see cref="ConcurrencyConflictException"/> (→ HTTP 409) on mismatch (AC-4).</item>
///   <item>Applies ONLY non-locked fields from <see cref="UpdatePatientProfileDto"/> —
///         locked fields (Name, DateOfBirth, BiologicalSex) are never touched (AC-3).</item>
///   <item>PHI fields (Phone, Address, EmergencyContact) are explicitly encrypted via
///         <see cref="IPhiEncryptionService.Encrypt"/> before persisting (NFR-004, NFR-013).</item>
///   <item>Persists via <see cref="IPatientRepository.UpdateAsync"/>.</item>
///   <item>Writes an immutable <see cref="AuditLog"/> entry with no PHI values in details
///         (HIPAA Minimum Necessary, AC-2).</item>
/// </list>
/// </summary>
public sealed class UpdatePatientProfileCommandHandler
    : IRequestHandler<UpdatePatientProfileCommand, UpdatePatientProfileResult>
{
    private readonly IPatientRepository _patientRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IPhiEncryptionService _phiEncryption;
    private readonly ILogger<UpdatePatientProfileCommandHandler> _logger;

    public UpdatePatientProfileCommandHandler(
        IPatientRepository patientRepo,
        IAuditLogRepository auditLogRepo,
        IPhiEncryptionService phiEncryption,
        ILogger<UpdatePatientProfileCommandHandler> logger)
    {
        _patientRepo = patientRepo;
        _auditLogRepo = auditLogRepo;
        _phiEncryption = phiEncryption;
        _logger = logger;
    }

    public async Task<UpdatePatientProfileResult> Handle(
        UpdatePatientProfileCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1: Load patient — 404 if not found
        var patient = await _patientRepo.GetByIdAsync(request.PatientId, cancellationToken);
        if (patient is null)
        {
            _logger.LogWarning(
                "Patient profile update failed — PatientId {PatientId} not found",
                request.PatientId);
            throw new KeyNotFoundException($"Patient {request.PatientId} not found.");
        }

        // Step 2: Optimistic concurrency check via xmin ETag (AC-4)
        var currentETag = Convert.ToBase64String(BitConverter.GetBytes(patient.RowVersion));
        if (!string.IsNullOrEmpty(request.IfMatchETag) && currentETag != request.IfMatchETag)
        {
            _logger.LogWarning(
                "Concurrency conflict on Patient {PatientId}: ETag mismatch",
                request.PatientId);
            throw new ConcurrencyConflictException(currentETag);
        }

        // Step 3: Apply ONLY non-locked fields (AC-3 — locked fields are never in UpdatePatientProfileDto)
        var dto = request.Payload;
        var updatedFields = new List<string>();

        if (dto.Phone is not null)
        {
            // Phone is PHI — already handled via EF Core value converter in AppDbContext (NFR-004)
            patient.Phone = dto.Phone;
            updatedFields.Add(nameof(dto.Phone));
        }

        if (dto.Address is not null)
        {
            // Address is PHI — serialise then encrypt before storing (NFR-004, NFR-013)
            var addressJson = JsonSerializer.Serialize(
                new PatientAddress(dto.Address.Street, dto.Address.City,
                    dto.Address.State, dto.Address.PostalCode, dto.Address.Country));
            patient.AddressEncrypted = _phiEncryption.Encrypt(addressJson);
            updatedFields.Add(nameof(dto.Address));
        }

        if (dto.EmergencyContact is not null)
        {
            // EmergencyContact is PHI — serialise then encrypt before storing (NFR-004, NFR-013)
            var ecJson = JsonSerializer.Serialize(
                new PatientEmergencyContact(dto.EmergencyContact.Name,
                    dto.EmergencyContact.Phone, dto.EmergencyContact.Relationship));
            patient.EmergencyContactEncrypted = _phiEncryption.Encrypt(ecJson);
            updatedFields.Add(nameof(dto.EmergencyContact));
        }

        if (dto.CommunicationPreferences is not null)
        {
            // CommunicationPreferences is non-PHI — plain JSON serialisation
            patient.CommunicationPreferencesJson = JsonSerializer.Serialize(
                new PatientCommunicationPreferences(
                    dto.CommunicationPreferences.EmailEnabled,
                    dto.CommunicationPreferences.SmsEnabled,
                    dto.CommunicationPreferences.PushEnabled));
            updatedFields.Add(nameof(dto.CommunicationPreferences));
        }

        if (dto.InsurerName is not null)
        {
            patient.InsurerName = dto.InsurerName;
            updatedFields.Add(nameof(dto.InsurerName));
        }

        if (dto.MemberId is not null)
        {
            patient.MemberId = dto.MemberId;
            updatedFields.Add(nameof(dto.MemberId));
        }

        if (dto.GroupNumber is not null)
        {
            patient.GroupNumber = dto.GroupNumber;
            updatedFields.Add(nameof(dto.GroupNumber));
        }

        // Step 4: Persist — EF Core value converters re-encrypt PHI Phone/Name/DateOfBirth
        // transparently; Address/EmergencyContact already stored as encrypted strings above (NFR-004)
        await _patientRepo.UpdateAsync(patient, cancellationToken);

        // Step 5: Audit log — no PHI values, only field names (HIPAA Minimum Necessary, AC-2)
        var auditDetails = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            updatedFields
        }));

        await _auditLogRepo.AppendAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = request.PatientId,
            PatientId = request.PatientId,
            Action = "PatientProfileUpdated",
            EntityType = nameof(PatientEntity),
            EntityId = request.PatientId,
            Details = auditDetails,
            IpAddress = request.IpAddress,
            CorrelationId = request.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "Patient {PatientId} profile updated. Fields: {Fields}",
            request.PatientId, string.Join(", ", updatedFields));

        // Step 6: Compute refreshed ETag from updated xmin
        var refreshedETag = Convert.ToBase64String(BitConverter.GetBytes(patient.RowVersion));

        // Decrypt/deserialise Address and EmergencyContact for the response DTO
        AddressDto? addressDto = null;
        if (!string.IsNullOrEmpty(patient.AddressEncrypted))
        {
            var json = _phiEncryption.Decrypt(patient.AddressEncrypted);
            var addr = JsonSerializer.Deserialize<PatientAddress>(json);
            if (addr is not null)
                addressDto = new AddressDto(addr.Street, addr.City, addr.State, addr.PostalCode, addr.Country);
        }

        EmergencyContactDto? emergencyContactDto = null;
        if (!string.IsNullOrEmpty(patient.EmergencyContactEncrypted))
        {
            var json = _phiEncryption.Decrypt(patient.EmergencyContactEncrypted);
            var ec = JsonSerializer.Deserialize<PatientEmergencyContact>(json);
            if (ec is not null)
                emergencyContactDto = new EmergencyContactDto(ec.Name, ec.Phone, ec.Relationship);
        }

        CommunicationPreferencesDto? commPrefsDto = null;
        if (!string.IsNullOrEmpty(patient.CommunicationPreferencesJson))
        {
            var cp = JsonSerializer.Deserialize<PatientCommunicationPreferences>(
                patient.CommunicationPreferencesJson);
            if (cp is not null)
                commPrefsDto = new CommunicationPreferencesDto(cp.EmailEnabled, cp.SmsEnabled, cp.PushEnabled);
        }

        var resultDto = new PatientProfileDto(
            Id: patient.Id,
            Name: patient.Name,
            DateOfBirth: patient.DateOfBirth,
            BiologicalSex: patient.BiologicalSex,
            Email: patient.Email,
            Phone: string.IsNullOrEmpty(patient.Phone) ? null : patient.Phone,
            Address: addressDto,
            EmergencyContact: emergencyContactDto,
            CommunicationPreferences: commPrefsDto,
            InsurerName: patient.InsurerName,
            MemberId: patient.MemberId,
            GroupNumber: patient.GroupNumber);

        return new UpdatePatientProfileResult(resultDto, refreshedETag);
    }
}
