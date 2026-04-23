import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Validates a phone number against the E.164 international format.
 * Accepts an optional leading '+' followed by 2–15 digits (ITU-T E.164).
 *
 * Valid examples: +12025550123, +442071838750, 12025550123
 * Invalid examples: 555-0123, (800) 555-0123, abc
 */
export function e164PhoneValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: string = (control.value ?? '').trim();

    // Allow empty — use Validators.required separately when the field is mandatory
    if (!value) {
      return null;
    }

    const e164Pattern = /^\+?[1-9]\d{1,14}$/;

    return e164Pattern.test(value) ? null : { e164Phone: true };
  };
}
