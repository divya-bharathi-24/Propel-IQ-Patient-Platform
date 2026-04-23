using Propel.Domain.Entities;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Repository abstraction for appointment booking write operations (US_019, task_002).
/// Implementations live in the infrastructure layer (Propel.Api.Gateway) and use
/// EF Core with <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> detection
/// to enforce the unique partial index on <c>(specialty_id, date, time_slot_start)</c>.
/// </summary>
public interface IAppointmentBookingRepository
{
    /// <summary>
    /// Inserts a new <see cref="Appointment"/> record and returns the persisted entity.
    /// Implementations catch <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>
    /// caused by the unique partial index on <c>(specialty_id, date, time_slot_start)</c>
    /// and throw <c>SlotConflictException</c> so callers do not need an EF Core dependency.
    /// </summary>
    Task<Appointment> CreateAppointmentAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts an <see cref="InsuranceValidation"/> record linked to the appointment.
    /// Called immediately after a successful <see cref="CreateAppointmentAsync"/>.
    /// </summary>
    Task CreateInsuranceValidationAsync(
        InsuranceValidation validation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a <see cref="WaitlistEntry"/> when the patient designated a preferred slot
    /// (DR-003 FIFO ordering). Only called when <c>PreferredSlotId</c> is non-null.
    /// </summary>
    Task CreateWaitlistEntryAsync(
        WaitlistEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the display name of the specialty for the booking confirmation response.
    /// Returns <c>null</c> if the specialty is not found.
    /// </summary>
    Task<string?> GetSpecialtyNameAsync(
        Guid specialtyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an <see cref="Appointment"/> by its primary key, eagerly including:
    /// <list type="bullet">
    ///   <item><see cref="Notification"/> records with <c>status = Pending</c></item>
    ///   <item><see cref="WaitlistEntry"/> records with <c>status = Active</c></item>
    ///   <item>The <see cref="CalendarSync"/> record with <c>syncStatus = Synced</c> (if any)</item>
    /// </list>
    /// Returns <c>null</c> if no appointment exists with the given <paramref name="appointmentId"/>.
    /// Used by <c>CancelAppointmentCommandHandler</c> (US_020, AC-1, AC-2, AC-4).
    /// </summary>
    Task<Appointment?> GetByIdWithRelatedAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all tracked entity changes (cancellation status updates, notification suppression,
    /// waitlist cancellation) in a single atomic <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync"/> call.
    /// Used by <c>CancelAppointmentCommandHandler</c> to commit all mutations atomically (US_020, AC-1).
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the given slot already has a <c>Booked</c> or <c>Arrived</c>
    /// appointment, indicating the slot is no longer available for a new booking.
    /// Used by <c>RescheduleAppointmentCommandHandler</c> as an optimistic pre-check before
    /// the atomic cancel+create transaction (US_020, AC-3, task_003).
    /// </summary>
    Task<bool> IsSlotTakenAsync(
        Guid specialtyId,
        DateOnly date,
        TimeOnly timeSlotStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new <see cref="Appointment"/> entity to the EF Core change tracker without
    /// calling <c>SaveChangesAsync</c>. Used by <c>RescheduleAppointmentCommandHandler</c> to
    /// stage the new booking alongside the original appointment cancellation so both mutations
    /// are committed in a single <see cref="SaveAsync"/> call (US_020, AC-3, task_003).
    /// </summary>
    void StageAppointment(Appointment appointment);

    /// <summary>
    /// Loads an <see cref="Appointment"/> by its primary key with the <see cref="Patient"/>
    /// and <see cref="Specialty"/> navigation properties eagerly included (<c>AsNoTracking</c>).
    /// Returns <c>null</c> when no appointment exists with the given <paramref name="appointmentId"/>.
    /// Used by <c>GetStaffAppointmentDetailQueryHandler</c> to project the appointment detail
    /// DTO including patient name and specialty name (US_034, AC-3).
    /// </summary>
    Task<Appointment?> GetByIdWithPatientAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
