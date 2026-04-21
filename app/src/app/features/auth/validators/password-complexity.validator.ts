import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

export interface PasswordComplexityErrors {
  minLength?: true;
  uppercase?: true;
  digit?: true;
  specialChar?: true;
}

/**
 * Validates password complexity and returns a map of violated rules.
 * Each key corresponds to a specific rule:
 * - minLength: at least 8 characters
 * - uppercase: at least one uppercase letter
 * - digit: at least one numeric digit
 * - specialChar: at least one special character
 */
export function passwordComplexityValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: string = control.value ?? '';

    const errors: PasswordComplexityErrors = {};

    if (value.length < 8) {
      errors.minLength = true;
    }

    if (!/[A-Z]/.test(value)) {
      errors.uppercase = true;
    }

    if (!/[0-9]/.test(value)) {
      errors.digit = true;
    }

    if (!/[^A-Za-z0-9]/.test(value)) {
      errors.specialChar = true;
    }

    return Object.keys(errors).length > 0 ? errors : null;
  };
}
