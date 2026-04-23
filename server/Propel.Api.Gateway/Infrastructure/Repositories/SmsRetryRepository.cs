using Microsoft.EntityFrameworkCore;
using Propel.Api.Gateway.Data;
using Propel.Domain.Entities;
using Propel.Domain.Enums;

namespace Propel.Api.Gateway.Infrastructure.Repositories;

/// <summary>
/// Query repository for SMS retry-eligible <see cref="Notification"/> records (US_025, AC-3).
/// Returns records that satisfy the single-retry constraint and the 5-minute elapsed window.
/// Uses <see cref="IDbContextFactory{TContext}"/> to create an isolated read scope consistent
/// with the AD-7 non-request-scoped pattern used by the rest of the infrastructure layer.
/// This class intentionally exposes no write methods — all state mutations are performed by
/// <see cref="NotificationRetryCommandHandler"/> in its own isolated DbContext scope.
/// </summary>
public sealed class SmsRetryRepository
{
    private const string SlotSwapTemplate = "SlotSwapNotification";

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SmsRetryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Returns all <see cref="Notification"/> records currently eligible for one SMS retry attempt.
    /// Eligibility criteria (AC-3):
    /// <list type="bullet">
    ///   <item><c>Channel = SMS</c></item>
    ///   <item><c>Status = Failed</c></item>
    ///   <item><c>RetryCount = 0</c> — enforces the at-most-once retry guarantee; records with
    ///         <c>RetryCount = 1</c> are permanently excluded regardless of their final status.</item>
    ///   <item><c>TemplateType = "SlotSwapNotification"</c></item>
    ///   <item><c>SentAt &lt;= UtcNow - 5 minutes</c> — enforces the minimum 5-minute retry window
    ///         defined in AC-3 so retries never fire before the window has elapsed.</item>
    /// </list>
    /// No PHI is returned in the result set. The caller reconstructs the SMS payload by
    /// fetching the linked <see cref="Appointment"/> record via <c>AppointmentId</c>.
    /// </summary>
    public async Task<IReadOnlyList<Notification>> GetRetryEligibleSmsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // The cutoff is materialised into a local variable so EF Core generates a
        // parameterised query rather than translating DateTime.UtcNow.AddMinutes(-5)
        // as a function call, which is not always supported by all EF Core providers.
        var cutoff = DateTime.UtcNow.AddMinutes(-5);

        return await context.Notifications
            .Where(n =>
                n.Channel == NotificationChannel.Sms &&
                n.Status == NotificationStatus.Failed &&
                n.RetryCount == 0 &&
                n.TemplateType == SlotSwapTemplate &&
                n.SentAt != null &&
                n.SentAt <= cutoff)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
