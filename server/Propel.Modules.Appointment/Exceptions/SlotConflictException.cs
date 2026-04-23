namespace Propel.Modules.Appointment.Exceptions;

/// <summary>
/// Thrown when a concurrent booking attempt violates the unique partial index on
/// <c>(specialty_id, date, time_slot_start)</c> in the <c>appointments</c> table.
/// Maps to HTTP 409 Conflict with body <c>{"code":"SLOT_CONFLICT","message":"Slot no longer available"}</c>
/// (US_019, AC-3).
/// </summary>
public sealed class SlotConflictException : Exception
{
    public SlotConflictException(string message) : base(message)
    {
    }
}
