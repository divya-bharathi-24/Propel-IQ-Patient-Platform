namespace Propel.Modules.Calendar.Dtos;

/// <summary>
/// Outcome of a single external calendar API call made by a calendar adapter (us_037, AC-1 to AC-3).
/// Returned by <c>IGoogleCalendarAdapter</c> and <c>IOutlookCalendarAdapter</c> methods so that
/// <c>CalendarPropagationService</c> can determine the correct <c>CalendarSync.syncStatus</c>
/// without holding a hard dependency on Google or Microsoft SDK exception types.
/// </summary>
public sealed class CalendarApiResult
{
    /// <summary>Whether the API call completed without error.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>Whether the API responded with HTTP 401 Unauthorized (token expired/revoked).</summary>
    public bool IsUnauthorized { get; private init; }

    /// <summary>
    /// Whether the API call was a DELETE operation.
    /// Used to derive <c>syncStatus = Revoked</c> (AC-2) vs <c>Synced</c> (AC-1).
    /// </summary>
    public bool WasDelete { get; private init; }

    /// <summary>HTTP status code returned by the provider, or <c>0</c> when not applicable.</summary>
    public int StatusCode { get; private init; }

    /// <summary>Error detail when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; private init; }

    private CalendarApiResult() { }

    /// <summary>Factory for a successful update (PATCH) result.</summary>
    public static CalendarApiResult Synced() =>
        new() { IsSuccess = true, WasDelete = false };

    /// <summary>Factory for a successful delete (DELETE) result.</summary>
    public static CalendarApiResult Revoked() =>
        new() { IsSuccess = true, WasDelete = true };

    /// <summary>Factory for an HTTP 401 Unauthorized response.</summary>
    public static CalendarApiResult Unauthorized(int statusCode = 401) =>
        new() { IsSuccess = false, IsUnauthorized = true, StatusCode = statusCode };

    /// <summary>Factory for any non-auth API failure (5xx, timeout, etc.).</summary>
    public static CalendarApiResult Failure(string errorMessage, int statusCode = 0) =>
        new() { IsSuccess = false, IsUnauthorized = false, StatusCode = statusCode, ErrorMessage = errorMessage };
}
