using System.Text.Json;
using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="UpdateIntakeCommand"/> (US_017, AC-2, AC-3).
/// <para>
/// Checks that all required demographic fields are present inside the <c>Demographics</c>
/// JSONB payload. This validator is injected directly into
/// <see cref="Handlers.UpdateIntakeCommandHandler"/> and called <em>manually</em> after the
/// optimistic concurrency check — it is NOT auto-invoked by the ASP.NET Core filter pipeline.
/// </para>
/// <para>
/// On failure the handler persists partial form data as a draft (AC-3) and throws
/// <see cref="Exceptions.IntakeMissingFieldsException"/> (→ HTTP 422) instead of the standard
/// HTTP 400 produced by <c>GlobalExceptionFilter</c> for auto-validated commands.
/// </para>
/// Required fields: <c>demographics.name</c>, <c>demographics.dob</c>,
/// <c>demographics.sex</c>, <c>demographics.phone</c>.
/// </summary>
public sealed class UpdateIntakeRequestValidator : AbstractValidator<UpdateIntakeCommand>
{
    public UpdateIntakeRequestValidator()
    {
        RuleFor(x => x.AppointmentId)
            .NotEmpty().WithMessage("appointmentId must not be empty.");

        RuleFor(x => x.Demographics)
            .NotNull().WithMessage("demographics is required.");

        When(x => x.Demographics is not null, () =>
        {
            RuleFor(x => x)
                .Must(x => HasNonEmptyStringProperty(x.Demographics!, "name"))
                .WithName("demographics.name")
                .WithMessage("demographics.name is required.");

            RuleFor(x => x)
                .Must(x => HasNonEmptyStringProperty(x.Demographics!, "dob"))
                .WithName("demographics.dob")
                .WithMessage("demographics.dob is required.");

            RuleFor(x => x)
                .Must(x => HasNonEmptyStringProperty(x.Demographics!, "sex"))
                .WithName("demographics.sex")
                .WithMessage("demographics.sex is required.");

            RuleFor(x => x)
                .Must(x => HasNonEmptyStringProperty(x.Demographics!, "phone"))
                .WithName("demographics.phone")
                .WithMessage("demographics.phone is required.");
        });
    }

    private static bool HasNonEmptyStringProperty(JsonDocument doc, string propertyName)
    {
        if (!doc.RootElement.TryGetProperty(propertyName, out var prop))
            return false;

        if (prop.ValueKind != JsonValueKind.String)
            return false;

        return !string.IsNullOrWhiteSpace(prop.GetString());
    }
}
