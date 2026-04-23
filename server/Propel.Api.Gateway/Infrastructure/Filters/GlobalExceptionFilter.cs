using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Propel.Api.Gateway.Features.Documents.Exceptions;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Modules.Admin.Exceptions;
using Propel.Modules.AI.Exceptions;
using Propel.Modules.Appointment.Exceptions;
using Propel.Modules.Auth.Exceptions;
using Propel.Modules.Patient.Exceptions;
using Propel.Modules.Queue.Exceptions;
using Propel.Modules.Risk.Exceptions;

namespace Propel.Api.Gateway.Infrastructure.Filters;

/// <summary>
/// MVC action filter that maps FluentValidation failures and known domain exceptions to
/// structured HTTP responses with no stack traces exposed (NFR-014, TR-020, AC-2).
/// <para>
/// Runs inside the MVC action filter pipeline so it fires before
/// <see cref="Propel.Api.Gateway.Middleware.ExceptionHandlingMiddleware"/>, which catches
/// exceptions originating outside the MVC layer (routing, middleware, etc.).
/// </para>
/// </summary>
public sealed class GlobalExceptionFilter : IAsyncExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        // Guard: do not handle if response has already started
        if (context.HttpContext.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response — headers already sent for {Method} {Path}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);
            return Task.CompletedTask;
        }

        var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? string.Empty;

        // ── FluentValidation failure → structured 400 with per-field errors ───
        if (context.Exception is ValidationException validationEx)
        {
            var errors = validationEx.Errors
                .Select(e => new FieldError(e.PropertyName, e.ErrorMessage))
                .ToList();

            _logger.LogWarning(
                "Validation failed on {Method} {Path}: {ErrorCount} error(s) [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                errors.Count,
                correlationId);

            context.Result = new BadRequestObjectResult(new ValidationErrorResponse(errors));
            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Slot booking / reschedule conflict — 409 with SLOT_CONFLICT code (US_019, US_020, AC-3) ─
        if (context.Exception is SlotConflictException slotEx)
        {
            _logger.LogWarning(
                "SlotConflictException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, correlationId);

            context.Result = new ObjectResult(new
            {
                code = "SLOT_CONFLICT",
                message = slotEx.Message,
                correlationId
            })
            { StatusCode = StatusCodes.Status409Conflict };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Walk-in patient duplicate email — special 409 with existingPatientId payload ─
        if (context.Exception is WalkInPatientDuplicateEmailException walkInEx)
        {
            _logger.LogWarning(
                "WalkInPatientDuplicateEmailException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, correlationId);

            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.com/409",
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                detail = "Email already registered",
                existingPatientId = walkInEx.ExistingPatientId,
                correlationId
            })
            { StatusCode = StatusCodes.Status409Conflict };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Optimistic concurrency conflict — 409 with currentETag for client retry (US_015, AC-4) ─
        if (context.Exception is Propel.Modules.Patient.Exceptions.ConcurrencyConflictException concurrencyEx)
        {
            _logger.LogWarning(
                "ConcurrencyConflictException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, correlationId);

            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.com/409",
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                message = "Conflict",
                currentETag = concurrencyEx.CurrentETag,
                correlationId
            })
            { StatusCode = StatusCodes.Status409Conflict };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Intake optimistic concurrency conflict — 409 with server-side IntakeRecordDto (US_017, AC-2) ─
        if (context.Exception is Propel.Modules.Patient.Exceptions.IntakeConcurrencyConflictException intakeConcurrencyEx)
        {
            _logger.LogWarning(
                "IntakeConcurrencyConflictException on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path, correlationId);

            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.com/409",
                title = "Conflict",
                status = StatusCodes.Status409Conflict,
                message = "The intake record was modified by another request.",
                currentVersion = intakeConcurrencyEx.CurrentVersion,
                correlationId
            })
            { StatusCode = StatusCodes.Status409Conflict };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Intake missing required fields — 422 with missingFields[] and partial draft saved (US_017, AC-3) ─
        if (context.Exception is Propel.Modules.Patient.Exceptions.IntakeMissingFieldsException missingFieldsEx)
        {
            _logger.LogInformation(
                "IntakeMissingFieldsException on {Method} {Path}: {MissingFields} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path,
                string.Join(", ", missingFieldsEx.MissingFields), correlationId);

            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.com/422",
                title = "Unprocessable Entity",
                status = StatusCodes.Status422UnprocessableEntity,
                message = "One or more required intake fields are missing. Draft has been saved.",
                missingFields = missingFieldsEx.MissingFields,
                correlationId
            })
            { StatusCode = StatusCodes.Status422UnprocessableEntity };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Intake forbidden — 403 when patient attempts to access another patient's record (US_030, OWASP A01) ─
        if (context.Exception is Propel.Modules.Patient.Exceptions.IntakeForbiddenException intakeForbiddenEx)
        {
            _logger.LogWarning(
                "IntakeForbiddenException on {Method} {Path}: {Message} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path,
                intakeForbiddenEx.Message, correlationId);

            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.com/403",
                title = "Forbidden",
                status = StatusCodes.Status403Forbidden,
                detail = intakeForbiddenEx.Message,
                correlationId
            })
            { StatusCode = StatusCodes.Status403Forbidden };

            context.ExceptionHandled = true;
            return Task.CompletedTask;
        }

        // ── Known domain exceptions → mapped HTTP codes ───────────────────────
        (int statusCode, string detail) = context.Exception switch
        {
            DuplicateEmailException => (StatusCodes.Status409Conflict, context.Exception.Message),
            DuplicateUserEmailException => (StatusCodes.Status409Conflict, context.Exception.Message),
            TokenAlreadyUsedException => (StatusCodes.Status409Conflict, context.Exception.Message),
            TokenExpiredException => (StatusCodes.Status410Gone, context.Exception.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "The requested resource was not found."),
            // ── US_045 — Admin user management domain exceptions ───────────────
            SelfDeactivationException => (StatusCodes.Status422UnprocessableEntity, context.Exception.Message),
            UserDeactivatedException => (StatusCodes.Status422UnprocessableEntity, context.Exception.Message),
            // ── US_046 — Re-auth token and role assignment exceptions (AC-2, AC-3) ─
            Propel.Modules.Admin.Exceptions.ReAuthFailedException => (StatusCodes.Status401Unauthorized, context.Exception.Message),
            // ── US_020 — Appointment cancellation domain exceptions ────────────
            BusinessRuleViolationException => (StatusCodes.Status400BadRequest, context.Exception.Message),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, context.Exception.Message),
            // ── US_027 — Queue arrived / revert domain exceptions ─────────────
            QueueBusinessRuleViolationException => (StatusCodes.Status400BadRequest, context.Exception.Message),
            // ── US_032 — Risk flag intervention domain exceptions ──────────────
            RiskBusinessRuleViolationException => (StatusCodes.Status400BadRequest, context.Exception.Message),
            // ── US_028 — AI intake domain exceptions ───────────────────────────
            AiIntakeDuplicateException => (StatusCodes.Status409Conflict, context.Exception.Message),
            AiForbiddenAccessException => (StatusCodes.Status403Forbidden, context.Exception.Message),
            // ── US_038 / US_039 — Clinical document domain exceptions ──────────
            StorageUnavailableException => (StatusCodes.Status503ServiceUnavailable, context.Exception.Message),
            DocumentForbiddenException => (StatusCodes.Status403Forbidden, context.Exception.Message),
            _ => (StatusCodes.Status500InternalServerError,
                  "An unexpected error occurred. Please try again later.")
        };

        // Log unhandled exceptions with full detail; domain errors at Warning (no PII in message).
        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(context.Exception,
                "Unhandled exception on {Method} {Path} [CorrelationId: {CorrelationId}]",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                correlationId);
        }
        else
        {
            _logger.LogWarning(
                "{ExceptionType} on {Method} {Path}: {Message} [CorrelationId: {CorrelationId}]",
                context.Exception.GetType().Name,
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                context.Exception.Message,
                correlationId);
        }

        context.Result = new ObjectResult(new
        {
            type = $"https://httpstatuses.com/{statusCode}",
            title = GetTitle(statusCode),
            status = statusCode,
            detail,
            correlationId
        })
        { StatusCode = statusCode };

        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Validation Error",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        410 => "Gone",
        422 => "Unprocessable Entity",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };
}
