namespace Propel.Modules.Appointment.Exceptions;

/// <summary>
/// Thrown by <c>ExecuteSlotSwapCommandHandler</c> when all three deadlock-retry attempts are
/// exhausted without a successful commit (US_024, AC-2 edge case).
/// The caller (<c>SlotReleasedEventHandler</c>) catches this exception and the
/// <c>WaitlistEntry</c> remains <c>Active</c> for future resolution.
/// </summary>
public sealed class SlotSwapPermanentFailureException : Exception
{
    /// <summary>Primary key of the WaitlistEntry that could not be swapped.</summary>
    public Guid WaitlistEntryId { get; }

    public SlotSwapPermanentFailureException(Guid waitlistEntryId)
        : base($"Slot swap permanently failed for WaitlistEntry '{waitlistEntryId}' after 3 deadlock retries.")
    {
        WaitlistEntryId = waitlistEntryId;
    }
}
