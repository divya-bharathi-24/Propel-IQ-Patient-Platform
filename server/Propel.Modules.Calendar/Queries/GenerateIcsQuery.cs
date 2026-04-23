using MediatR;
using Propel.Domain.Entities;
using Propel.Modules.Calendar.Interfaces;

namespace Propel.Modules.Calendar.Queries;

/// <summary>
/// Generates a RFC 5545-compliant ICS file for the given appointment (us_035, us_036, AC-3).
/// The handler validates appointment ownership before generating the ICS bytes.
/// </summary>
public sealed record GenerateIcsQuery(Guid AppointmentId) : IRequest<byte[]>;
