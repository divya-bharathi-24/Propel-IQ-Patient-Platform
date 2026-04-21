using System.Text.Json;
using FluentValidation;
using Propel.Api.Gateway.Infrastructure.Models;
using Propel.Modules.Admin.Exceptions;
using Propel.Modules.Auth.Exceptions;
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
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? string.Empty;

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
        _ => "Internal Server Error"
    };
}
