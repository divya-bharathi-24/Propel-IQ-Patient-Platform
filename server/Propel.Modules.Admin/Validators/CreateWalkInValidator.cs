using FluentValidation;
using Propel.Modules.Admin.Commands;
using Propel.Modules.Admin.Enums;

namespace Propel.Modules.Admin.Validators;

/// <summary>
/// FluentValidation validator for <see cref="CreateWalkInCommand"/> (US_026, AC-2, AC-3).
/// <para>
/// Conditional rules per mode:
/// <list type="bullet">
///   <item><c>Link</c>: <see cref="CreateWalkInCommand.PatientId"/> must be a non-empty GUID.</item>
///   <item><c>Create</c>: <see cref="CreateWalkInCommand.Name"/> required (max 200);
///         <see cref="CreateWalkInCommand.Email"/> required and valid format;
///         <see cref="CreateWalkInCommand.ContactNumber"/> optional — E.164 regex when provided.</item>
///   <item><c>Anonymous</c>: no patient fields required.</item>
/// </list>
/// </para>
/// <c>SpecialtyId</c> and <c>Date</c> are always required.
/// <c>StaffId</c> is intentionally absent — resolved from JWT inside the handler (OWASP A01).
/// </summary>
public sealed class CreateWalkInValidator : AbstractValidator<CreateWalkInCommand>
{
    // E.164 international phone number format: +[country code][number], 8–15 digits
    private static readonly System.Text.RegularExpressions.Regex E164Regex =
        new(@"^\+[1-9]\d{7,14}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> ValidModes =
        new(StringComparer.OrdinalIgnoreCase) { "link", "create", "anonymous" };

    public CreateWalkInValidator()
    {
        // Mode must be parseable
        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("'Mode' must be one of: Link, Create, Anonymous.");

        // SpecialtyId: always required
        RuleFor(x => x.SpecialtyId)
            .NotEmpty()
            .WithMessage("'SpecialtyId' must not be empty.");

        // Date: always today or a future date
        RuleFor(x => x.Date)
            .NotEmpty()
            .Must(date => date >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("'Date' must be today or a future date.");

        // TimeSlotEnd must be provided when TimeSlotStart is provided
        When(x => x.TimeSlotStart.HasValue, () =>
        {
            RuleFor(x => x.TimeSlotEnd)
                .NotNull()
                .WithMessage("'TimeSlotEnd' is required when 'TimeSlotStart' is provided.");
        });

        // Mode = Link: PatientId required
        When(x => x.Mode == WalkInMode.Link, () =>
        {
            RuleFor(x => x.PatientId)
                .NotNull().WithMessage("'PatientId' is required for mode 'Link'.")
                .NotEqual(Guid.Empty).WithMessage("'PatientId' must not be an empty GUID.");
        });

        // Mode = Create: Name and Email required; ContactNumber validated if provided
        When(x => x.Mode == WalkInMode.Create, () =>
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("'Name' is required for mode 'Create'.")
                .MaximumLength(200).WithMessage("'Name' must not exceed 200 characters.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("'Email' is required for mode 'Create'.")
                .EmailAddress().WithMessage("'Email' is not a valid email address.")
                .MaximumLength(320).WithMessage("'Email' must not exceed 320 characters.");

            When(x => !string.IsNullOrWhiteSpace(x.ContactNumber), () =>
            {
                RuleFor(x => x.ContactNumber!)
                    .Matches(E164Regex)
                    .WithMessage("'ContactNumber' must be in E.164 format (e.g. +12025551234).");
            });
        });
    }
}
