import * as fs from 'fs';
import * as path from 'path';

const STORE_PATH = path.join(__dirname, '..', '.auth', 'registered-user.json');

export interface RegisteredUser {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

/** Persists the registered user credentials to .auth/registered-user.json */
export function saveRegisteredUser(user: RegisteredUser): void {
  const dir = path.dirname(STORE_PATH);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.writeFileSync(STORE_PATH, JSON.stringify(user, null, 2), 'utf-8');
}

/** Returns the stored registered user, or null if none has been saved yet. */
export function loadRegisteredUser(): RegisteredUser | null {
  if (!fs.existsSync(STORE_PATH)) {
    return null;
  }
  return JSON.parse(fs.readFileSync(STORE_PATH, 'utf-8')) as RegisteredUser;
}
