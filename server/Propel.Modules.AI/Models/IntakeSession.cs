namespace Propel.Modules.AI.Models;

/// <summary>
/// A single conversational turn in the AI intake session (US_028, AC-2).
/// <c>Role</c> is either <c>"user"</c> (patient input) or <c>"assistant"</c> (AI response).
/// </summary>
public sealed record ConversationTurn(string Role, string Content);

/// <summary>
/// A structured field extracted by the AI NLU layer from patient utterances (US_028, AC-2, AC-3).
/// <para>
/// Fields with <c>Confidence</c> below 0.8 carry <c>NeedsClarification = true</c> per AIR-003.
/// The AI response for that turn includes a targeted follow-up question.
/// </para>
/// <para>
/// <c>FieldName</c> convention: <c>{group}.{name}</c> where <c>group</c> is one of
/// <c>demographics</c>, <c>medicalHistory</c>, <c>symptoms</c>, <c>medications</c>.
/// This allows <c>SubmitAiIntakeCommandHandler</c> to map fields into the correct
/// JSONB column on <c>IntakeRecord</c> without an additional schema lookup.
/// </para>
/// </summary>
public sealed record ExtractedField(
    string FieldName,
    string Value,
    double Confidence,
    bool NeedsClarification);

/// <summary>
/// In-memory AI intake session tracking conversation history and extracted fields
/// for a single patient-appointment pairing (US_028, AC-1 – AC-4).
/// <para>
/// Stored in <c>IntakeSessionStore</c> (singleton <c>ConcurrentDictionary</c>).
/// Idle sessions are evicted after 60 minutes by a background timer.
/// </para>
/// </summary>
public sealed class IntakeSession
{
    public Guid SessionId { get; init; }
    public Guid PatientId { get; init; }
    public Guid AppointmentId { get; init; }

    /// <summary>Ordered conversation history. Appended on each turn by <c>IntakeSessionStore</c>.</summary>
    public List<ConversationTurn> History { get; init; } = [];

    /// <summary>
    /// Latest extracted fields merged from all turns. Updated by <c>IntakeSessionStore.MergeFields</c>
    /// which upserts by <c>FieldName</c> so later turns overwrite earlier low-confidence values.
    /// </summary>
    public List<ExtractedField> ExtractedFields { get; init; } = [];

    public DateTime CreatedAt { get; init; }

    /// <summary>Updated on every read/write operation; used by the idle-expiry timer.</summary>
    public DateTime LastAccessedAt { get; set; }
}
