/** Where a demo visitor goes to create a real cabinet when the server names none. */
export const DEFAULT_DEMO_SIGNUP_URL = "https://apexa.tn/#telecharger"

/** The one sentence every refused save shows on the demo (spec AC-3.1); the API client puts it on the error. */
export const DEMO_READ_ONLY_MESSAGE =
  "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données."

export const DEMO_SIGNUP_LABEL = "Créer mon cabinet"

let signupUrl = DEFAULT_DEMO_SIGNUP_URL

/** Set once by `DemoProvider` from `GET /api/auth/mode`, for the toast, which renders outside React. */
export function rememberDemoSignupUrl(url: string) {
  signupUrl = url || DEFAULT_DEMO_SIGNUP_URL
}

export function demoSignupUrl(): string {
  return signupUrl
}

/** The only test for a demo refusal: the client replaced the message, so identity is exact. */
export function isDemoRefusal(message: string | null | undefined): boolean {
  return message?.trim() === DEMO_READ_ONLY_MESSAGE
}
