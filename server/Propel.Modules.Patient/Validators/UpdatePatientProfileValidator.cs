using FluentValidation;
using Propel.Modules.Patient.Commands;

namespace Propel.Modules.Patient.Validators;

/// <summary>
/// FluentValidation validator for <see cref="UpdatePatientProfileCommand"/> (US_015, AC-4).
/// <para>
/// All fields are optional per PATCH semantics — rules only fire when the field is provided.
/// Invalid values return HTTP 400 with per-field error messages via <c>GlobalExceptionFilter</c>.
/// </para>
/// Validation rules:
/// <list type="bullet">
///   <item><c>Phone</c> — E.164 international format (e.g. +12025550123) when provided.</item>
///   <item><c>Address.*</c> — MaxLength(200) per field when provided.</item>
///   <item><c>EmergencyContact.Name</c> — MaxLength(200) when provided.</item>
///   <item><c>EmergencyContact.Phone</c> — same E.164 regex when provided.</item>
///   <item><c>EmergencyContact.Relationship</c> — MaxLength(100) when provided.</item>
///   <item><c>InsurerName</c>, <c>MemberId</c>, <c>GroupNumber</c> — MaxLength(200) each when provided.</item>
/// </list>
/// </summary>
public sealed class UpdatePatientProfileValidator : AbstractValidator<UpdatePatientProfileCommand>
{
    private const string PhoneRegex = @"^\+?[1-9]\d{1,14}$";
    private const string PhoneMessage =
        "Phone must be in international format (e.g. +1-202-555-0123)";

    public UpdatePatientProfileValidator()
    {
        RuleFor(x => x.Payload.Phone)
            .Matches(PhoneRegex).WithMessage(PhoneMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Payload.Phone));

        When(x => x.Payload.Address is not null, () =>
        {
            RuleFor(x => x.Payload.Address!.Street)
                .MaximumLength(200).WithMessage("Address.Street must not exceed 200 characters.")
                .When(x => x.Payload.Address!.Street is not null);

            RuleFor(x => x.Payload.Address!.City)
                .MaximumLength(200).WithMessage("Address.City must not exceed 200 characters.")
                .When(x => x.Payload.Address!.City is not null);

            RuleFor(x => x.Payload.Address!.State)
                .MaximumLength(200).WithMessage("Address.State must not exceed 200 characters.")
                .When(x => x.Payload.Address!.State is not null);

            RuleFor(x => x.Payload.Address!.PostalCode)
                .MaximumLength(200).WithMessage("Address.PostalCode must not exceed 200 characters.")
                .When(x => x.Payload.Address!.PostalCode is not null);

            RuleFor(x => x.Payload.Address!.Country)
                .MaximumLength(200).WithMessage("Address.Country must not exceed 200 characters.")
                .When(x => x.Payload.Address!.Country is not null);
        });

        When(x => x.Payload.EmergencyContact is not null, () =>
        {
            RuleFor(x => x.Payload.EmergencyContact!.Name)
                .MaximumLength(200).WithMessage("EmergencyContact.Name must not exceed 200 characters.")
                .When(x => x.Payload.EmergencyContact!.Name is not null);

            RuleFor(x => x.Payload.EmergencyContact!.Phone)
                .Matches(PhoneRegex).WithMessage(PhoneMessage)
                .When(x => !string.IsNullOrWhiteSpace(x.Payload.EmergencyContact!.Phone));

            RuleFor(x => x.Payload.EmergencyContact!.Relationship)
                .MaximumLength(100).WithMessage("EmergencyContact.Relationship must not exceed 100 characters.")
                .When(x => x.Payload.EmergencyContact!.Relationship is not null);
        });

        RuleFor(x => x.Payload.InsurerName)
            .MaximumLength(200).WithMessage("InsurerName must not exceed 200 characters.")
            .When(x => x.Payload.InsurerName is not null);

        RuleFor(x => x.Payload.MemberId)
            .MaximumLength(200).WithMessage("MemberId must not exceed 200 characters.")
            .When(x => x.Payload.MemberId is not null);

        RuleFor(x => x.Payload.GroupNumber)
            .MaximumLength(200).WithMessage("GroupNumber must not exceed 200 characters.")
            .When(x => x.Payload.GroupNumber is not null);
    }
}
