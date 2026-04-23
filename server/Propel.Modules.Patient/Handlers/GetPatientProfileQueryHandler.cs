using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Propel.Domain.Interfaces;
using Propel.Modules.Patient.Dtos;
using Propel.Modules.Patient.Queries;

namespace Propel.Modules.Patient.Handlers;

/// <summary>
/// Handles <see cref="GetPatientProfileQuery"/> for <c>GET /api/patients/me</c> (US_015, AC-1).
/// <list type="number">
///   <item>Loads the patient by Id via <see cref="IPatientRepository.GetByIdAsync"/>.</item>
///   <item>Decrypts PHI fields (AddressEncrypted, EmergencyContactEncrypted) via
///         <see cref="IPhiEncryptionService.Decrypt"/> and deserialises JSON (NFR-004, NFR-013).</item>
///   <item>Maps to <see cref="PatientProfileDto"/> and computes the ETag from <c>xmin</c>.</item>
/// </list>
/// </summary>
public sealed class GetPatientProfileQueryHandler
    : IRequestHandler<GetPatientProfileQuery, GetPatientProfileResult>
{
    private readonly IPatientRepository _patientRepo;
    private readonly IPhiEncryptionService _phiEncryption;
    private readonly ILogger<GetPatientProfileQueryHandler> _logger;

    public GetPatientProfileQueryHandler(
        IPatientRepository patientRepo,
        IPhiEncryptionService phiEncryption,
        ILogger<GetPatientProfileQueryHandler> logger)
    {
        _patientRepo = patientRepo;
        _phiEncryption = phiEncryption;
        _logger = logger;
    }

    public async Task<GetPatientProfileResult> Handle(
        GetPatientProfileQuery request,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepo.GetByIdAsync(request.PatientId, cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "Patient profile not found for PatientId {PatientId}", request.PatientId);
            throw new KeyNotFoundException($"Patient {request.PatientId} not found.");
        }

        var etag = Convert.ToBase64String(BitConverter.GetBytes(patient.RowVersion));

        // Decrypt and deserialise PHI address JSON if present (NFR-004, NFR-013)
        AddressDto? addressDto = null;
        if (!string.IsNullOrEmpty(patient.AddressEncrypted))
        {
            var json = _phiEncryption.Decrypt(patient.AddressEncrypted);
            var addr = JsonSerializer.Deserialize<PatientAddress>(json);
            if (addr is not null)
                addressDto = new AddressDto(addr.Street, addr.City, addr.State, addr.PostalCode, addr.Country);
        }

        // Decrypt and deserialise PHI emergency contact JSON if present
        EmergencyContactDto? emergencyContactDto = null;
        if (!string.IsNullOrEmpty(patient.EmergencyContactEncrypted))
        {
            var json = _phiEncryption.Decrypt(patient.EmergencyContactEncrypted);
            var ec = JsonSerializer.Deserialize<PatientEmergencyContact>(json);
            if (ec is not null)
                emergencyContactDto = new EmergencyContactDto(ec.Name, ec.Phone, ec.Relationship);
        }

        // Deserialise non-PHI communication preferences JSON if present
        CommunicationPreferencesDto? commPrefsDto = null;
        if (!string.IsNullOrEmpty(patient.CommunicationPreferencesJson))
        {
            var cp = JsonSerializer.Deserialize<PatientCommunicationPreferences>(
                patient.CommunicationPreferencesJson);
            if (cp is not null)
                commPrefsDto = new CommunicationPreferencesDto(cp.EmailEnabled, cp.SmsEnabled, cp.PushEnabled);
        }

        var dto = new PatientProfileDto(
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

        _logger.LogInformation(
            "Patient profile retrieved for PatientId {PatientId}", request.PatientId);

        return new GetPatientProfileResult(dto, etag);
    }
}
