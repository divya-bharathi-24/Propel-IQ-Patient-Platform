using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Modules.Admin.Exceptions;
using Propel.Modules.Auth.Exceptions;
using Propel.Modules.Patient.Exceptions;

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

        // ── Known domain exceptions → mapped HTTP codes ───────────────────────
        (int statusCode, string detail) = context.Exception switch
        {
            DuplicateEmailException => (StatusCodes.Status409Conflict, context.Exception.Message),
            DuplicateUserEmailException => (StatusCodes.Status409Conflict, context.Exception.Message),
            TokenAlreadyUsedException => (StatusCodes.Status409Conflict, context.Exception.Message),
            TokenExpiredException => (StatusCodes.Status410Gone, context.Exception.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "The requested resource was not found."),
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
        404 => "Not Found",
        409 => "Conflict",
        410 => "Gone",
        _ => "Internal Server Error"
    };
}
