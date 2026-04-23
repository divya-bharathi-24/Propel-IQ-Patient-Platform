using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Propel.Modules.AI.Models;

namespace Propel.Modules.AI.Services;

/// <summary>
/// Singleton in-memory store for active AI intake sessions (US_028, AC-1 – AC-4).
/// <para>
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe access under
/// concurrent patient requests. A background <see cref="Timer"/> fires every 5 minutes to
/// evict sessions idle for more than 60 minutes, preventing unbounded memory growth.
/// </para>
/// <para>
/// Sessions are NOT persisted across process restarts. Patients who lose their session
/// mid-intake can start a new one via <c>POST /api/intake/ai/session</c>; any extracted
/// fields accumulated in the previous session are not recoverable — this is an acceptable
/// trade-off given the 60-minute expiry window (US_028 scope).
/// </para>
/// </summary>
public sealed class IntakeSessionStore : IDisposable
{
    private readonly ConcurrentDictionary<Guid, IntakeSession> _sessions = new();
    private readonly ILogger<IntakeSessionStore> _logger;
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan IdleExpiry = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    public IntakeSessionStore(ILogger<IntakeSessionStore> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(RunCleanup, null, CleanupInterval, CleanupInterval);
    }

    /// <summary>
    /// Creates a new intake session tied to <paramref name="patientId"/> and
    /// <paramref name="appointmentId"/>. Returns the new <c>sessionId</c>.
    /// </summary>
    public Guid CreateSession(Guid patientId, Guid appointmentId)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var session = new IntakeSession
        {
            SessionId = sessionId,
            PatientId = patientId,
            AppointmentId = appointmentId,
            CreatedAt = now,
            LastAccessedAt = now
        };

        _sessions[sessionId] = session;

        _logger.LogInformation(
            "IntakeSession {SessionId} created for PatientId={PatientId} AppointmentId={AppointmentId}",
            sessionId, patientId, appointmentId);

        return sessionId;
    }

    /// <summary>
    /// Returns the session for <paramref name="sessionId"/>, or <c>null</c> if not found
    /// or already evicted. Updates <c>LastAccessedAt</c> on successful retrieval.
    /// </summary>
    public IntakeSession? GetSession(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        session.LastAccessedAt = DateTime.UtcNow;
        return session;
    }

    /// <summary>
    /// Appends a <see cref="ConversationTurn"/> to the session's history.
    /// No-op if the session has been evicted.
    /// </summary>
    public void AddTurn(Guid sessionId, ConversationTurn turn)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        lock (session.History)
        {
            session.History.Add(turn);
        }

        session.LastAccessedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Merges extracted fields into the session's running field state, upserting by
    /// <c>FieldName</c> so that later turns overwrite earlier lower-confidence values.
    /// No-op if the session has been evicted.
    /// </summary>
    public void MergeFields(Guid sessionId, IReadOnlyList<ExtractedField> newFields)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        lock (session.ExtractedFields)
        {
            foreach (var incoming in newFields)
            {
                var existing = session.ExtractedFields
                    .FindIndex(f => string.Equals(f.FieldName, incoming.FieldName, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                    session.ExtractedFields[existing] = incoming;
                else
                    session.ExtractedFields.Add(incoming);
            }
        }

        session.LastAccessedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes and disposes the session after a successful submit.
    /// </summary>
    public bool Remove(Guid sessionId)
    {
        var removed = _sessions.TryRemove(sessionId, out _);
        if (removed)
        {
            _logger.LogInformation(
                "IntakeSession {SessionId} removed after successful submit.", sessionId);
        }

        return removed;
    }

    private void RunCleanup(object? state)
    {
        var cutoff = DateTime.UtcNow - IdleExpiry;
        var evicted = 0;

        foreach (var (sessionId, session) in _sessions)
        {
            if (session.LastAccessedAt < cutoff)
            {
                if (_sessions.TryRemove(sessionId, out _))
                    evicted++;
            }
        }

        if (evicted > 0)
        {
            _logger.LogInformation(
                "IntakeSessionStore cleanup: evicted {Count} idle session(s) older than {Expiry} minutes.",
                evicted, IdleExpiry.TotalMinutes);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
