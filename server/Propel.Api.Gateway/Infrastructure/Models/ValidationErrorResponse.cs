namespace Propel.Api.Gateway.Infrastructure.Models;

/// <summary>
/// Canonical HTTP 400 response body for FluentValidation failures (AC-2, NFR-014, TR-020).
/// Shape: <c>{ "errors": [{ "field": "Email", "message": "must not be empty" }] }</c>
/// No stack traces or internal exception details are ever exposed.
/// </summary>
public sealed record ValidationErrorResponse(IEnumerable<FieldError> Errors);

/// <summary>Single field-level validation failure within a <see cref="ValidationErrorResponse"/>.</summary>
public sealed record FieldError(string Field, string Message);
