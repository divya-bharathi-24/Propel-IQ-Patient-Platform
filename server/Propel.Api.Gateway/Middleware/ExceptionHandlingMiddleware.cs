using System.Text.Json;
using FluentValidation;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Modules.Admin.Exceptions;
using Propel.Modules.Appointment.Exceptions;
using Propel.Modules.Auth.Exceptions;
using Propel.Modules.Notification.Exceptions;
using Propel.Modules.Patient.Exceptions;

namespace Propel.Api.Gateway.Middleware;

/// <summary>
/// Global exception handler middleware that maps domain exceptions to HTTP status codes.
/// No stack traces or internal exception details are ever included in responses (NFR-014).
/// </summary>
public sealed class ExceptionHandlingMiddleware : IMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Guard: do not write to response if headers have already been sent
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response � headers already sent for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            return;
        }

        var correlationId = context.Items["CorrelationId"]?.ToString() ?? string.Empty;

        // Reminder debounce cooldown: 429 with retryAfterSeconds (US_034, AC-2 edge case).
        if (exception is ReminderCooldownException cooldownEx)
        {
            _logger.LogWarning(
                "ReminderCooldownException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers.RetryAfter = cooldownEx.RetryAfterSeconds.ToString();

            var cooldownProblem = new
            {
                type = "https://httpstatuses.com/429",
                title = "Too Many Requests",
                status = StatusCodes.Status429TooManyRequests,
                detail = cooldownEx.Message,
                retryAfterSeconds = cooldownEx.RetryAfterSeconds,
                correlationId
            };

            await context.Response.WriteAsJsonAsync(cooldownProblem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return;
        }

        // Cancelled appointment reminder: 422 Unprocessable Entity (US_034, AC-1 edge case).
        if (exception is CancelledAppointmentReminderException cancelledEx)
        {
            _logger.LogWarning(
                "CancelledAppointmentReminderException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "application/problem+json";

            var cancelledProblem = new
            {
                type = "https://httpstatuses.com/422",
                title = "Unprocessable Entity",
                status = StatusCodes.Status422UnprocessableEntity,
                detail = cancelledEx.Message,
                correlationId
            };

            await context.Response.WriteAsJsonAsync(cancelledProblem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return;
        }

        // Slot booking conflict: 409 with structured SLOT_CONFLICT code (US_019, AC-3)
        if (exception is SlotConflictException slotEx)
        {
            _logger.LogWarning(
                "SlotConflictException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var slotConflictProblem = new
            {
                code = "SLOT_CONFLICT",
                message = slotEx.Message,
                correlationId
            };

            await context.Response.WriteAsJsonAsync(slotConflictProblem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return;
        }

        // Walk-in patient duplicate email: 409 with existingPatientId payload (US_012, AC-3)
        if (exception is WalkInPatientDuplicateEmailException walkInEx)
        {
            _logger.LogWarning(
                "WalkInPatientDuplicateEmailException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var conflictProblem = new
            {
                type = "https://httpstatuses.com/409",
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                detail = "Email already registered",
                existingPatientId = walkInEx.ExistingPatientId,
                correlationId
            };

            await context.Response.WriteAsJsonAsync(conflictProblem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return;
        }

        (int statusCode, string message) = exception switch
        {
            ValidationException => (
                StatusCodes.Status400BadRequest,
                string.Empty),  // handled separately below with structured { errors } response

            DuplicateEmailException => (
                StatusCodes.Status409Conflict,
                exception.Message),

            DuplicateUserEmailException => (
                StatusCodes.Status409Conflict,
                exception.Message),

            TokenAlreadyUsedException => (
                StatusCodes.Status409Conflict,
                exception.Message),

            TokenExpiredException => (
                StatusCodes.Status410Gone,
                exception.Message),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "The requested resource was not found."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred. Please try again later.")
        };

        // Log unexpected errors with full context; expected domain errors at Warning/Info
        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception,
                "Unhandled exception on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);
        }
        else
        {
            _logger.LogWarning(
                "{ExceptionType} on {Method} {Path}: {Message} [CorrelationId: {CorrelationId}]",
                exception.GetType().Name, context.Request.Method,
                context.Request.Path, exception.Message, correlationId);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        // ValidationException: emit structured { errors: [{ field, message }] } (AC-2, NFR-014).
        if (exception is ValidationException ve)
        {
            var validationResponse = new ValidationErrorResponse(
                ve.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)));
            await context.Response.WriteAsJsonAsync(validationResponse,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return;
        }

        var problem = new
        {
            type = $"https://httpstatuses.com/{statusCode}",
            title = GetTitle(statusCode),
            status = statusCode,
            detail = message,
            correlationId
        };

        await context.Response.WriteAsJsonAsync(problem,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Validation Error",
        404 => "Not Found",
        409 => "Conflict",
        410 => "Gone",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        _ => "Internal Server Error"
    };
}
